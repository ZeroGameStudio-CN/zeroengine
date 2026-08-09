using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class ConfigJsonParser
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static ConfigNode Parse(byte[] utf8Json)
        {
            if (utf8Json == null)
            {
                throw new ArgumentNullException(nameof(utf8Json));
            }

            return Parse(StrictUtf8.GetString(utf8Json));
        }

        public static ConfigNode Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            RejectComments(json);
            using (var textReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(textReader)
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 128,
                SupportMultipleContent = false
            })
            {
                JToken token = JToken.Load(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Load,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Load
                    });

                if (jsonReader.Read())
                {
                    throw new JsonReaderException("JSON contains trailing content.");
                }

                return ConvertToken(token);
            }
        }

        private static void RejectComments(string json)
        {
            using (var textReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(textReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                MaxDepth = 128,
                SupportMultipleContent = false
            })
            {
                while (jsonReader.Read())
                {
                    if (jsonReader.TokenType == JsonToken.Comment)
                    {
                        throw new JsonReaderException("JSON comments are not supported.");
                    }
                }
            }
        }

        private static ConfigNode ConvertToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Null:
                    return ConfigNullNode.Instance;
                case JTokenType.Boolean:
                    return new ConfigBooleanNode(token.Value<bool>());
                case JTokenType.Integer:
                    object integerValue = ((JValue)token).Value;
                    if (integerValue is BigInteger bigInteger)
                    {
                        if (bigInteger < long.MinValue || bigInteger > long.MaxValue)
                        {
                            throw new OverflowException("JSON integer is outside the supported int64 range.");
                        }

                        return new ConfigIntegerNode((long)bigInteger);
                    }

                    return new ConfigIntegerNode(Convert.ToInt64(integerValue, CultureInfo.InvariantCulture));
                case JTokenType.Float:
                    return new ConfigNumberNode(token.Value<double>());
                case JTokenType.String:
                    return new ConfigStringNode(token.Value<string>());
                case JTokenType.Array:
                    var items = new List<ConfigNode>();
                    foreach (JToken child in token.Children())
                    {
                        items.Add(ConvertToken(child));
                    }

                    return new ConfigArrayNode(items);
                case JTokenType.Object:
                    var properties = new List<ConfigProperty>();
                    foreach (JProperty property in ((JObject)token).Properties())
                    {
                        properties.Add(new ConfigProperty(property.Name, ConvertToken(property.Value)));
                    }

                    return new ConfigObjectNode(properties);
                default:
                    throw new JsonReaderException("Unsupported JSON token type: " + token.Type + ".");
            }
        }
    }
}
