using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    public sealed class ConfigEffectiveValue
    {
        internal ConfigEffectiveValue(
            string targetScope,
            string artifactPath,
            string jsonPath,
            string canonicalValue,
            XlsxSourceMapEntry source)
        {
            TargetScope = targetScope;
            ArtifactPath = artifactPath;
            JsonPath = jsonPath;
            CanonicalValue = canonicalValue;
            SourceKind = source.SourceKind;
            SourceJsonPath = source.SourceJsonPath;
            SchemaPath = source.SchemaPath;
            Workbook = source.Workbook;
            Sheet = source.Sheet;
            Row = source.Row;
            Column = source.Column;
        }

        public string TargetScope { get; }
        public string ArtifactPath { get; }
        public string JsonPath { get; }
        public string CanonicalValue { get; }
        public ConfigValueSourceKind SourceKind { get; }
        public string SourceJsonPath { get; }
        public string SchemaPath { get; }
        public string Workbook { get; }
        public string Sheet { get; }
        public int Row { get; }
        public int Column { get; }

        public bool HasEditableInstanceCell =>
            SourceKind == ConfigValueSourceKind.Instance &&
            !string.IsNullOrEmpty(Workbook) &&
            !string.IsNullOrEmpty(Sheet) &&
            Row > 0 &&
            Column > 0;
    }

    internal static class ConfigEffectiveValueBuilder
    {
        public static IReadOnlyList<ConfigEffectiveValue> Build(
            string targetScope,
            string artifactPath,
            ConfigDocument document,
            ConfigSchema schema,
            IReadOnlyList<XlsxSourceMapEntry> sourceMap)
        {
            return ConfigSourceMapBuilder.Build(document, schema, sourceMap)
                .Select(source => new ConfigEffectiveValue(
                    targetScope,
                    artifactPath,
                    source.JsonPath,
                    CanonicalJsonWriter.WriteText(Resolve(document.Root, source.JsonPath)).Trim(),
                    source))
                .OrderBy(value => value.JsonPath, StringComparer.Ordinal)
                .ToArray();
        }

        private static ConfigNode Resolve(ConfigNode root, string jsonPath)
        {
            if (string.Equals(jsonPath, "$", StringComparison.Ordinal))
            {
                return root;
            }

            if (string.IsNullOrEmpty(jsonPath) ||
                !jsonPath.StartsWith("$/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid effective-value JSON path: " + jsonPath);
            }

            ConfigNode current = root;
            foreach (string encodedSegment in jsonPath.Substring(2).Split('/'))
            {
                string segment = encodedSegment.Replace("~1", "/").Replace("~0", "~");
                if (current is ConfigObjectNode objectNode)
                {
                    if (!objectNode.TryGetValue(segment, out current))
                    {
                        throw new InvalidOperationException(
                            "Effective-value path is missing object field: " + jsonPath);
                    }
                }
                else if (current is ConfigArrayNode arrayNode &&
                         int.TryParse(
                             segment,
                             NumberStyles.None,
                             CultureInfo.InvariantCulture,
                             out int index) &&
                         index >= 0 &&
                         index < arrayNode.Items.Count)
                {
                    current = arrayNode.Items[index];
                }
                else
                {
                    throw new InvalidOperationException(
                        "Effective-value path cannot be resolved: " + jsonPath);
                }
            }

            return current;
        }
    }
}
