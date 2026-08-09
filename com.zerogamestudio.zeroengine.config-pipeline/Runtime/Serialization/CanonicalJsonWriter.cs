using System;
using System.IO;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline
{
    public static class CanonicalJsonWriter
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        public static string WriteText(ConfigNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            var builder = new StringBuilder();
            WriteNode(builder, node, 0);
            builder.Append('\n');
            return builder.ToString();
        }

        public static byte[] WriteUtf8(ConfigNode node)
        {
            return Utf8WithoutBom.GetBytes(WriteText(node));
        }

        public static void Write(Stream destination, ConfigNode node)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            byte[] bytes = WriteUtf8(node);
            destination.Write(bytes, 0, bytes.Length);
        }

        private static void WriteNode(StringBuilder builder, ConfigNode node, int indent)
        {
            switch (node.Kind)
            {
                case ConfigNodeKind.Null:
                    builder.Append("null");
                    return;
                case ConfigNodeKind.Boolean:
                    builder.Append(((ConfigBooleanNode)node).Value ? "true" : "false");
                    return;
                case ConfigNodeKind.Integer:
                    builder.Append(CanonicalNumberWriter.Write(((ConfigIntegerNode)node).Value));
                    return;
                case ConfigNodeKind.Number:
                    var number = (ConfigNumberNode)node;
                    builder.Append(number.NumberType == ConfigNumberType.Float32
                        ? CanonicalNumberWriter.Write(number.Float32Value)
                        : CanonicalNumberWriter.Write(number.Value));
                    return;
                case ConfigNodeKind.String:
                    WriteString(builder, ((ConfigStringNode)node).Value);
                    return;
                case ConfigNodeKind.Array:
                    WriteArray(builder, (ConfigArrayNode)node, indent);
                    return;
                case ConfigNodeKind.Object:
                    WriteObject(builder, (ConfigObjectNode)node, indent);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(node), node.Kind, "Unknown config node kind.");
            }
        }

        private static void WriteArray(StringBuilder builder, ConfigArrayNode array, int indent)
        {
            if (array.Items.Count == 0)
            {
                builder.Append("[]");
                return;
            }

            builder.Append("[\n");
            for (int index = 0; index < array.Items.Count; index++)
            {
                AppendIndent(builder, indent + 1);
                WriteNode(builder, array.Items[index], indent + 1);
                if (index + 1 < array.Items.Count)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            AppendIndent(builder, indent);
            builder.Append(']');
        }

        private static void WriteObject(StringBuilder builder, ConfigObjectNode configObject, int indent)
        {
            if (configObject.Properties.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append("{\n");
            for (int index = 0; index < configObject.Properties.Count; index++)
            {
                ConfigProperty property = configObject.Properties[index];
                AppendIndent(builder, indent + 1);
                WriteString(builder, property.Name);
                builder.Append(": ");
                WriteNode(builder, property.Value, indent + 1);
                if (index + 1 < configObject.Properties.Count)
                {
                    builder.Append(',');
                }

                builder.Append('\n');
            }

            AppendIndent(builder, indent);
            builder.Append('}');
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4"));
                        }
                        else if (char.IsSurrogate(character))
                        {
                            if (!char.IsHighSurrogate(character) ||
                                index + 1 >= value.Length ||
                                !char.IsLowSurrogate(value[index + 1]))
                            {
                                throw new ArgumentException("Strings cannot contain unpaired UTF-16 surrogates.");
                            }

                            builder.Append(character);
                            builder.Append(value[++index]);
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void AppendIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }
    }
}
