using System;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigArtifactContract
    {
        public ConfigArtifactContract(
            string configSetId,
            string schemaId,
            int schemaVersion,
            string schemaHash)
        {
            ConfigSetId = configSetId ?? throw new ArgumentNullException(nameof(configSetId));
            SchemaId = schemaId ?? throw new ArgumentNullException(nameof(schemaId));
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            SchemaHash = schemaHash ?? throw new ArgumentNullException(nameof(schemaHash));
        }

        public string ConfigSetId { get; }

        public string SchemaId { get; }

        public int SchemaVersion { get; }

        public string SchemaHash { get; }
    }
}
