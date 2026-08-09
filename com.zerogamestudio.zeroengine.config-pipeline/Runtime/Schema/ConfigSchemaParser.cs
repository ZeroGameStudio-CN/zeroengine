using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class ConfigSchemaParser
    {
        private static readonly HashSet<string> SupportedKeywords =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "$schema",
                "$id",
                "$defs",
                "$ref",
                "type",
                "properties",
                "items",
                "required",
                "default",
                "enum",
                "minimum",
                "maximum",
                "exclusiveMinimum",
                "exclusiveMaximum",
                "pattern",
                "minItems",
                "maxItems",
                "uniqueItems",
                "additionalProperties",
                "title",
                "description",
                "x-zgs-schema-version",
                "x-zgs-sheet",
                "x-zgs-primary-key",
                "x-zgs-parent-key",
                "x-zgs-order-field",
                "x-zgs-number-type",
                "x-zgs-ref",
                "x-zgs-asset-type",
                "x-zgs-localization-key",
                "x-zgs-scope",
                "x-zgs-unit",
                "x-zgs-group",
                "x-zgs-authoring-only"
            };

        public static ConfigSchema Parse(byte[] utf8Schema)
        {
            if (utf8Schema == null)
            {
                throw new ArgumentNullException(nameof(utf8Schema));
            }

            ConfigNode genericRoot = ConfigJsonParser.Parse(utf8Schema);
            if (!(genericRoot is ConfigObjectNode sourceNode))
            {
                throw new ConfigSchemaException("SCHEMA_ROOT_INVALID", "$", "Schema root must be an object.");
            }

            JObject root = JObject.Parse(
                System.Text.Encoding.UTF8.GetString(utf8Schema),
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                });
            string schemaId = RequireString(root, "$id", "$");
            int schemaVersion = RequirePositiveInteger(root, "x-zgs-schema-version", "$");
            var definitions = root["$defs"] as JObject ?? new JObject();
            var resolvingReferences = new HashSet<string>(StringComparer.Ordinal);
            ConfigSchemaNode schemaRoot = ParseNode(
                root,
                "$",
                definitions,
                resolvingReferences,
                ConfigFieldScope.Shared);
            if (schemaRoot.Type != ConfigSchemaType.Object)
            {
                throw new ConfigSchemaException("SCHEMA_ROOT_TYPE_INVALID", "$", "Schema root type must be object.");
            }

            return new ConfigSchema(
                schemaId,
                schemaVersion,
                ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(genericRoot)),
                sourceNode,
                schemaRoot);
        }

        private static ConfigSchemaNode ParseNode(
            JObject source,
            string path,
            JObject definitions,
            HashSet<string> resolvingReferences,
            ConfigFieldScope inheritedScope)
        {
            RejectUnknownKeywords(source, path);
            if (source.TryGetValue("$ref", out JToken referenceToken))
            {
                if (source.Properties().Count() != 1)
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_REF_SIBLING_UNSUPPORTED",
                        path,
                        "$ref cannot have sibling keywords in the supported subset.");
                }

                string reference = RequireString(referenceToken, path + "/$ref");
                const string prefix = "#/$defs/";
                if (!reference.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_REF_EXTERNAL",
                        path,
                        "Only local $defs references are supported.");
                }

                string definitionName = reference.Substring(prefix.Length);
                if (definitionName.Length == 0 || definitionName.IndexOf('/') >= 0)
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_REF_INVALID",
                        path,
                        "Only direct local $defs references are supported.");
                }

                if (!resolvingReferences.Add(definitionName))
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_REF_CYCLE",
                        path,
                        "Recursive schema references are not supported.");
                }

                try
                {
                    if (!(definitions[definitionName] is JObject definition))
                    {
                        throw new ConfigSchemaException(
                            "SCHEMA_REF_MISSING",
                            path,
                            "Missing $defs entry '" + definitionName + "'.");
                    }

                    return ParseNode(
                        definition,
                        "#/$defs/" + definitionName,
                        definitions,
                        resolvingReferences,
                        inheritedScope);
                }
                finally
                {
                    resolvingReferences.Remove(definitionName);
                }
            }

            ConfigSchemaType type = ParseType(RequireString(source, "type", path), path);
            ConfigFieldScope scope = source.TryGetValue("x-zgs-scope", out JToken scopeToken)
                ? ParseScope(RequireString(scopeToken, path + "/x-zgs-scope"), path)
                : inheritedScope;
            bool authoringOnly = OptionalBoolean(source, "x-zgs-authoring-only", false, path);
            bool primaryKey = OptionalBoolean(source, "x-zgs-primary-key", false, path);
            bool localizationKey = OptionalBoolean(source, "x-zgs-localization-key", false, path);
            string referencePath = OptionalString(source, "x-zgs-ref", path);
            string sheet = OptionalString(source, "x-zgs-sheet", path);
            string parentKey = OptionalString(source, "x-zgs-parent-key", path);
            string orderField = OptionalString(source, "x-zgs-order-field", path);
            string assetType = OptionalString(source, "x-zgs-asset-type", path);
            ValidateAnnotationApplicability(
                source,
                type,
                path,
                primaryKey,
                localizationKey,
                referencePath,
                sheet,
                parentKey,
                orderField,
                assetType);

            var properties = new List<ConfigSchemaProperty>();
            var required = new List<string>();
            ConfigSchemaNode items = null;
            if (type == ConfigSchemaType.Object)
            {
                if (source["additionalProperties"]?.Type != JTokenType.Boolean ||
                    source["additionalProperties"].Value<bool>())
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_ADDITIONAL_PROPERTIES_REQUIRED",
                        path,
                        "Object schemas must set additionalProperties:false.");
                }

                if (source["properties"] is JObject propertyObject)
                {
                    foreach (JProperty property in propertyObject.Properties())
                    {
                        if (!(property.Value is JObject propertySchema))
                        {
                            throw new ConfigSchemaException(
                                "SCHEMA_PROPERTY_INVALID",
                                path + "/properties/" + property.Name,
                                "Property schemas must be objects.");
                        }

                        properties.Add(new ConfigSchemaProperty(
                            property.Name,
                            ParseNode(
                                propertySchema,
                                path + "/properties/" + property.Name,
                                definitions,
                                resolvingReferences,
                                scope)));
                    }
                }

                if (source["required"] is JArray requiredArray)
                {
                    var knownProperties = new HashSet<string>(
                        properties.Select(property => property.Name),
                        StringComparer.Ordinal);
                    foreach (JToken requiredToken in requiredArray)
                    {
                        string requiredName = RequireString(requiredToken, path + "/required");
                        if (!knownProperties.Contains(requiredName) || required.Contains(requiredName))
                        {
                            throw new ConfigSchemaException(
                                "SCHEMA_REQUIRED_INVALID",
                                path + "/required",
                                "Required entries must be unique declared properties.");
                        }

                        required.Add(requiredName);
                    }
                }
            }
            else if (type == ConfigSchemaType.Array)
            {
                if (!(source["items"] is JObject itemSchema))
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_ITEMS_REQUIRED",
                        path,
                        "Array schemas require one object-valued items schema.");
                }

                items = ParseNode(
                    itemSchema,
                    path + "/items",
                    definitions,
                    resolvingReferences,
                    scope);
            }

            ConfigIntegerType? integerType = ParseIntegerType(source, type, path);
            ConfigNumberType? numberType = ParseNumberType(source, type, path);
            double? minimum = OptionalFiniteNumber(source, "minimum", path);
            double? maximum = OptionalFiniteNumber(source, "maximum", path);
            double? exclusiveMinimum = OptionalFiniteNumber(source, "exclusiveMinimum", path);
            double? exclusiveMaximum = OptionalFiniteNumber(source, "exclusiveMaximum", path);
            if (minimum.HasValue && exclusiveMinimum.HasValue)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_RANGE_INVALID",
                    path,
                    "minimum and exclusiveMinimum cannot both be declared.");
            }

            if (maximum.HasValue && exclusiveMaximum.HasValue)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_RANGE_INVALID",
                    path,
                    "maximum and exclusiveMaximum cannot both be declared.");
            }

            double? effectiveMinimum = exclusiveMinimum ?? minimum;
            double? effectiveMaximum = exclusiveMaximum ?? maximum;
            if (effectiveMinimum.HasValue &&
                effectiveMaximum.HasValue &&
                effectiveMinimum.Value > effectiveMaximum.Value)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_RANGE_INVALID",
                    path,
                    "minimum cannot exceed maximum.");
            }

            int? minItems = OptionalNonNegativeInteger(source, "minItems", path);
            int? maxItems = OptionalNonNegativeInteger(source, "maxItems", path);
            if (minItems.HasValue && maxItems.HasValue && minItems.Value > maxItems.Value)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_ITEM_RANGE_INVALID",
                    path,
                    "minItems cannot exceed maxItems.");
            }

            ConfigNode defaultValue = source.TryGetValue("default", out JToken defaultToken)
                ? ConfigJsonParser.Parse(defaultToken.ToString(Newtonsoft.Json.Formatting.None))
                : null;
            var enumValues = new List<ConfigNode>();
            if (source["enum"] is JArray enumArray)
            {
                if (enumArray.Count == 0)
                {
                    throw new ConfigSchemaException("SCHEMA_ENUM_EMPTY", path, "enum cannot be empty.");
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (JToken enumToken in enumArray)
                {
                    ConfigNode value = ConfigJsonParser.Parse(
                        enumToken.ToString(Newtonsoft.Json.Formatting.None));
                    string canonical = CanonicalJsonWriter.WriteText(value);
                    if (!seen.Add(canonical))
                    {
                        throw new ConfigSchemaException(
                            "SCHEMA_ENUM_DUPLICATE",
                            path,
                            "enum values must be unique.");
                    }

                    enumValues.Add(value);
                }
            }

            return new ConfigSchemaNode(
                type,
                properties,
                required,
                items,
                defaultValue,
                enumValues,
                integerType,
                numberType,
                minimum,
                maximum,
                exclusiveMinimum,
                exclusiveMaximum,
                OptionalString(source, "pattern", path),
                minItems,
                maxItems,
                OptionalBoolean(source, "uniqueItems", false, path),
                scope,
                authoringOnly,
                primaryKey,
                referencePath,
                sheet,
                parentKey,
                orderField,
                assetType,
                localizationKey,
                OptionalString(source, "title", path),
                OptionalString(source, "description", path),
                OptionalString(source, "x-zgs-unit", path),
                OptionalString(source, "x-zgs-group", path));
        }

        private static void ValidateAnnotationApplicability(
            JObject source,
            ConfigSchemaType type,
            string path,
            bool primaryKey,
            bool localizationKey,
            string referencePath,
            string sheet,
            string parentKey,
            string orderField,
            string assetType)
        {
            if ((primaryKey ||
                 localizationKey ||
                 referencePath != null ||
                 assetType != null ||
                 source["pattern"] != null) &&
                type != ConfigSchemaType.String)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_STRING_ANNOTATION_INVALID",
                    path,
                    "Primary key, reference, asset, localization, and pattern annotations require a string field.");
            }

            if ((sheet != null || parentKey != null || orderField != null) &&
                type != ConfigSchemaType.Array)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_TABLE_ANNOTATION_INVALID",
                    path,
                    "Sheet, parent-key, and order-field annotations require an array field.");
            }

            bool hasNumberRange = source["minimum"] != null ||
                                  source["maximum"] != null ||
                                  source["exclusiveMinimum"] != null ||
                                  source["exclusiveMaximum"] != null;
            if (hasNumberRange &&
                type != ConfigSchemaType.Integer &&
                type != ConfigSchemaType.Number)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_NUMBER_RANGE_INVALID",
                    path,
                    "Numeric range keywords require an integer or number field.");
            }

            bool hasArrayKeyword = source["minItems"] != null ||
                                   source["maxItems"] != null ||
                                   source["uniqueItems"] != null ||
                                   source["items"] != null;
            if (hasArrayKeyword && type != ConfigSchemaType.Array)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_ARRAY_KEYWORD_INVALID",
                    path,
                    "Array keywords require an array field.");
            }

            bool hasObjectKeyword = source["properties"] != null ||
                                    source["required"] != null ||
                                    source["additionalProperties"] != null;
            if (hasObjectKeyword && type != ConfigSchemaType.Object)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_OBJECT_KEYWORD_INVALID",
                    path,
                    "Object keywords require an object field.");
            }
        }

        private static void RejectUnknownKeywords(JObject source, string path)
        {
            foreach (JProperty property in source.Properties())
            {
                if (!SupportedKeywords.Contains(property.Name))
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_KEYWORD_UNSUPPORTED",
                        path + "/" + property.Name,
                        "Unsupported schema keyword '" + property.Name + "'.");
                }
            }
        }

        private static ConfigSchemaType ParseType(string value, string path)
        {
            switch (value)
            {
                case "object":
                    return ConfigSchemaType.Object;
                case "array":
                    return ConfigSchemaType.Array;
                case "string":
                    return ConfigSchemaType.String;
                case "integer":
                    return ConfigSchemaType.Integer;
                case "number":
                    return ConfigSchemaType.Number;
                case "boolean":
                    return ConfigSchemaType.Boolean;
                default:
                    throw new ConfigSchemaException(
                        "SCHEMA_TYPE_UNSUPPORTED",
                        path + "/type",
                        "Unsupported schema type '" + value + "'.");
            }
        }

        private static ConfigNumberType? ParseNumberType(
            JObject source,
            ConfigSchemaType type,
            string path)
        {
            if (type != ConfigSchemaType.Integer && type != ConfigSchemaType.Number)
            {
                if (source["x-zgs-number-type"] != null)
                {
                    throw new ConfigSchemaException(
                        "SCHEMA_NUMBER_TYPE_INVALID",
                        path,
                        "x-zgs-number-type is only valid on integer and number fields.");
                }

                return null;
            }

            if (type == ConfigSchemaType.Integer)
            {
                return null;
            }

            string value = RequireString(source, "x-zgs-number-type", path);
            if (value == "float32")
            {
                return ConfigNumberType.Float32;
            }

            if (value == "float64")
            {
                return ConfigNumberType.Float64;
            }

            throw new ConfigSchemaException(
                "SCHEMA_NUMBER_TYPE_INVALID",
                path + "/x-zgs-number-type",
                "Number type '" + value + "' does not match schema type '" +
                type.ToString().ToLowerInvariant() + "'.");
        }

        private static ConfigIntegerType? ParseIntegerType(
            JObject source,
            ConfigSchemaType type,
            string path)
        {
            if (type != ConfigSchemaType.Integer)
            {
                return null;
            }

            string value = RequireString(source, "x-zgs-number-type", path);
            if (value == "int32")
            {
                return ConfigIntegerType.Int32;
            }

            if (value == "int64")
            {
                return ConfigIntegerType.Int64;
            }

            throw new ConfigSchemaException(
                "SCHEMA_NUMBER_TYPE_INVALID",
                path + "/x-zgs-number-type",
                "Integer number type must be int32 or int64.");
        }

        private static ConfigFieldScope ParseScope(string value, string path)
        {
            switch (value)
            {
                case "shared":
                    return ConfigFieldScope.Shared;
                case "client":
                    return ConfigFieldScope.Client;
                case "server":
                    return ConfigFieldScope.Server;
                default:
                    throw new ConfigSchemaException(
                        "SCHEMA_SCOPE_INVALID",
                        path + "/x-zgs-scope",
                        "Scope must be shared, client, or server.");
            }
        }

        private static string RequireString(JObject source, string propertyName, string path)
        {
            if (!source.TryGetValue(propertyName, out JToken token))
            {
                throw new ConfigSchemaException(
                    "SCHEMA_REQUIRED_KEYWORD_MISSING",
                    path,
                    "Missing required schema keyword '" + propertyName + "'.");
            }

            return RequireString(token, path + "/" + propertyName);
        }

        private static string RequireString(JToken token, string path)
        {
            if (token.Type != JTokenType.String || string.IsNullOrEmpty(token.Value<string>()))
            {
                throw new ConfigSchemaException(
                    "SCHEMA_STRING_INVALID",
                    path,
                    "Expected a non-empty string.");
            }

            return token.Value<string>();
        }

        private static string OptionalString(JObject source, string propertyName, string path)
        {
            return source.TryGetValue(propertyName, out JToken token)
                ? RequireString(token, path + "/" + propertyName)
                : null;
        }

        private static bool OptionalBoolean(
            JObject source,
            string propertyName,
            bool fallback,
            string path)
        {
            if (!source.TryGetValue(propertyName, out JToken token))
            {
                return fallback;
            }

            if (token.Type != JTokenType.Boolean)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_BOOLEAN_INVALID",
                    path + "/" + propertyName,
                    "Expected a boolean.");
            }

            return token.Value<bool>();
        }

        private static int RequirePositiveInteger(JObject source, string propertyName, string path)
        {
            int? value = OptionalNonNegativeInteger(source, propertyName, path);
            if (!value.HasValue || value.Value <= 0)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_VERSION_INVALID",
                    path + "/" + propertyName,
                    "Schema version must be a positive integer.");
            }

            return value.Value;
        }

        private static int? OptionalNonNegativeInteger(
            JObject source,
            string propertyName,
            string path)
        {
            if (!source.TryGetValue(propertyName, out JToken token))
            {
                return null;
            }

            if (token.Type != JTokenType.Integer)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_INTEGER_INVALID",
                    path + "/" + propertyName,
                    "Expected a non-negative integer.");
            }

            long value = token.Value<long>();
            if (value < 0 || value > int.MaxValue)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_INTEGER_INVALID",
                    path + "/" + propertyName,
                    "Expected a non-negative int32 value.");
            }

            return (int)value;
        }

        private static double? OptionalFiniteNumber(
            JObject source,
            string propertyName,
            string path)
        {
            if (!source.TryGetValue(propertyName, out JToken token))
            {
                return null;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw new ConfigSchemaException(
                    "SCHEMA_NUMBER_INVALID",
                    path + "/" + propertyName,
                    "Expected a finite number.");
            }

            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ConfigSchemaException(
                    "SCHEMA_NUMBER_INVALID",
                    path + "/" + propertyName,
                    "Expected a finite number.");
            }

            return value;
        }
    }

    public sealed class ConfigSchemaException : Exception
    {
        public ConfigSchemaException(string code, string path, string message)
            : base(message)
        {
            Code = code;
            Path = path;
        }

        public string Code { get; }

        public string Path { get; }
    }
}
