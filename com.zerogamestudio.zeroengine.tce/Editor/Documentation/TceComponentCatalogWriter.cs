using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace ZeroEngine.TCE.Editor
{
    public static class TceComponentCatalogWriter
    {
        public const string CatalogPath = "Packages/com.zerogamestudio.zeroengine.tce/Documentation~/component-catalog.md";
        public const string GraphSchemaPath = "Packages/com.zerogamestudio.zeroengine.tce/Documentation~/graph.schema.json";

        public static string WriteMarkdown(IReadOnlyList<TceComponentCatalogEntry> entries)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# ZeroEngine TCE Component Catalog");
            builder.AppendLine();

            foreach (IGrouping<TceComponentDocCategory, TceComponentCatalogEntry> group in entries.GroupBy(entry => entry.Category).OrderBy(group => group.Key))
            {
                builder.AppendLine($"## {group.Key}");
                builder.AppendLine();

                foreach (TceComponentCatalogEntry entry in group)
                {
                    builder.AppendLine($"### {entry.DisplayName}");
                    builder.AppendLine();
                    builder.AppendLine($"- Component ID: `{entry.ComponentId}`");
                    builder.AppendLine($"- Data type: `{entry.DataTypeFullName}`");
                    builder.AppendLine($"- Runtime type: `{entry.RuntimeTypeFullName}`");
                    builder.AppendLine($"- Summary: {entry.ShortDescription}");
                    builder.AppendLine($"- Description: {entry.ExpandedDescription}");

                    if (entry.Fields.Count == 0)
                    {
                        builder.AppendLine("- Fields: none");
                    }
                    else
                    {
                        builder.AppendLine("- Fields:");
                        foreach (TceComponentCatalogField field in entry.Fields)
                            builder.AppendLine($"  - `{field.Name}` (`{field.TypeName}`, default `{field.DefaultValue}`): {field.Description}");
                    }

                    builder.AppendLine();
                }
            }

            return builder.ToString().TrimEnd('\r', '\n').Replace("\r\n", "\n") + "\n";
        }

        public static string WriteGraphJsonSchema(IReadOnlyList<TceComponentCatalogEntry> entries)
        {
            entries ??= Array.Empty<TceComponentCatalogEntry>();

            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\",");
            builder.AppendLine("  \"$id\": \"https://zerogamestudio.local/schemas/zeroengine-tce-graph.schema.json\",");
            builder.AppendLine("  \"title\": \"ZeroEngine TCE Graph\",");
            builder.AppendLine("  \"type\": \"object\",");
            builder.AppendLine("  \"additionalProperties\": false,");
            builder.AppendLine("  \"required\": [\"format\", \"schemaVersion\", \"graphId\", \"displayName\", \"triggers\", \"conditions\", \"effects\"],");
            builder.AppendLine("  \"properties\": {");
            builder.AppendLine($"    \"format\": {{ \"const\": \"{EscapeJson(TceGraphSchema.Format)}\" }},");
            builder.AppendLine($"    \"schemaVersion\": {{ \"const\": {TceGraphSchema.CurrentVersion} }},");
            builder.AppendLine("    \"graphId\": { \"type\": \"string\", \"minLength\": 1 },");
            builder.AppendLine("    \"displayName\": { \"type\": \"string\" },");
            AppendLaneProperty(builder, "triggers", entries, TceComponentDocCategory.Trigger, true);
            AppendLaneProperty(builder, "conditions", entries, TceComponentDocCategory.Condition, true);
            AppendLaneProperty(builder, "effects", entries, TceComponentDocCategory.Effect, false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"$defs\": {");

            TceComponentCatalogEntry[] orderedEntries = entries
                .OrderBy(entry => entry.Category)
                .ThenBy(entry => entry.ComponentId, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < orderedEntries.Length; i++)
            {
                AppendComponentDefinition(builder, orderedEntries[i], i < orderedEntries.Length - 1);
            }

            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString().Replace("\r\n", "\n");
        }

        private static void AppendLaneProperty(
            StringBuilder builder,
            string propertyName,
            IReadOnlyList<TceComponentCatalogEntry> entries,
            TceComponentDocCategory category,
            bool appendComma)
        {
            builder.AppendLine($"    \"{propertyName}\": {{");
            builder.AppendLine("      \"type\": \"array\",");
            builder.AppendLine("      \"items\": {");
            builder.AppendLine("        \"oneOf\": [");

            TceComponentCatalogEntry[] laneEntries = entries
                .Where(entry => entry.Category == category)
                .OrderBy(entry => entry.ComponentId, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < laneEntries.Length; i++)
            {
                string comma = i < laneEntries.Length - 1 ? "," : string.Empty;
                builder.AppendLine($"          {{ \"$ref\": \"#/$defs/{ToDefinitionName(laneEntries[i].ComponentId)}\" }}{comma}");
            }

            builder.AppendLine("        ]");
            builder.AppendLine("      }");
            builder.AppendLine(appendComma ? "    }," : "    }");
        }

        private static void AppendComponentDefinition(StringBuilder builder, TceComponentCatalogEntry entry, bool appendComma)
        {
            builder.AppendLine($"    \"{ToDefinitionName(entry.ComponentId)}\": {{");
            builder.AppendLine("      \"type\": \"object\",");
            builder.AppendLine("      \"additionalProperties\": false,");
            builder.AppendLine("      \"required\": [\"componentId\", \"fields\"],");
            builder.AppendLine("      \"properties\": {");
            builder.AppendLine($"        \"componentId\": {{ \"const\": \"{EscapeJson(entry.ComponentId)}\" }},");
            builder.AppendLine("        \"fields\": {");
            builder.AppendLine("          \"type\": \"object\",");
            builder.AppendLine("          \"additionalProperties\": false,");
            builder.AppendLine("          \"properties\": {");

            for (int i = 0; i < entry.Fields.Count; i++)
            {
                TceComponentCatalogField field = entry.Fields[i];
                builder.AppendLine($"            \"{EscapeJson(field.Name)}\": {BuildFieldSchema(field)}{(i < entry.Fields.Count - 1 ? "," : string.Empty)}");
            }

            builder.AppendLine("          }");
            builder.AppendLine("        }");
            builder.AppendLine("      }");
            builder.AppendLine(appendComma ? "    }," : "    }");
        }

        private static string BuildFieldSchema(TceComponentCatalogField field)
        {
            var builder = new StringBuilder();
            builder.Append("{ ");
            builder.Append($"\"description\": \"{EscapeJson(field.Description)}\"");

            Type fieldType = field.FieldType;
            if (fieldType == typeof(string))
            {
                builder.Append(", \"type\": \"string\"");
            }
            else if (fieldType == typeof(bool))
            {
                builder.Append(", \"type\": \"boolean\"");
            }
            else if (fieldType == typeof(int) || fieldType == typeof(long) || fieldType == typeof(short) || fieldType == typeof(byte))
            {
                builder.Append(", \"type\": \"integer\"");
            }
            else if (fieldType == typeof(float) || fieldType == typeof(double))
            {
                builder.Append(", \"type\": \"number\"");
            }
            else if (fieldType != null && fieldType.IsEnum)
            {
                string values = string.Join(", ", Enum.GetNames(fieldType).Select(name => $"\"{EscapeJson(name)}\""));
                builder.Append(", \"type\": \"string\"");
                builder.Append($", \"enum\": [{values}]");
            }
            else
            {
                builder.Append(", \"type\": \"object\"");
            }

            builder.Append(" }");
            return builder.ToString();
        }

        private static string ToDefinitionName(string componentId)
        {
            return (componentId ?? string.Empty).Replace('.', '_');
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
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
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        [MenuItem("ZGS/ZeroEngine/TCE/Regenerate Component Catalog")]
        public static void RegenerateComponentCatalog()
        {
            IReadOnlyList<TceComponentCatalogEntry> entries = TceComponentCatalogBuilder.Build();
            string markdown = WriteMarkdown(entries);
            string schema = WriteGraphJsonSchema(entries);
            string directory = Path.GetDirectoryName(CatalogPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(CatalogPath, markdown, Encoding.UTF8);
            File.WriteAllText(GraphSchemaPath, schema, Encoding.UTF8);
            AssetDatabase.Refresh();
        }
    }
}
