using System;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigDocument
    {
        public ConfigDocument(
            string configSetId,
            string schemaId,
            int schemaVersion,
            ConfigObjectNode root)
        {
            ConfigSetId = RequireStableId(configSetId, nameof(configSetId));
            SchemaId = RequireStableId(schemaId, nameof(schemaId));
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be positive.");
            }

            SchemaVersion = schemaVersion;
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public string ConfigSetId { get; }

        public string SchemaId { get; }

        public int SchemaVersion { get; }

        public ConfigObjectNode Root { get; }

        private static string RequireStableId(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Stable IDs cannot be empty or contain leading/trailing whitespace.",
                    parameterName);
            }

            return value;
        }
    }
}
