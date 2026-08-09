using System;
using System.Collections.Generic;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigManifest
    {
        public const int CurrentFormatVersion = 1;

        public ConfigManifest(
            string configSetId,
            string schemaId,
            int schemaVersion,
            string toolVersion,
            string schemaHash,
            string baseSourceHash,
            string sourceHash,
            string artifactHash,
            string artifactPath,
            string targetScope)
        {
            FormatVersion = CurrentFormatVersion;
            ConfigSetId = RequireText(configSetId, nameof(configSetId));
            SchemaId = RequireText(schemaId, nameof(schemaId));
            if (schemaVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            }

            SchemaVersion = schemaVersion;
            ToolVersion = RequireText(toolVersion, nameof(toolVersion));
            SchemaHash = RequireHash(schemaHash, nameof(schemaHash));
            BaseSourceHash = RequireHash(baseSourceHash, nameof(baseSourceHash));
            SourceHash = RequireHash(sourceHash, nameof(sourceHash));
            ArtifactHash = RequireHash(artifactHash, nameof(artifactHash));
            ArtifactPath = RequireRelativePath(artifactPath, nameof(artifactPath));
            TargetScope = RequireText(targetScope, nameof(targetScope));
        }

        public int FormatVersion { get; }

        public string ConfigSetId { get; }

        public string SchemaId { get; }

        public int SchemaVersion { get; }

        public string ToolVersion { get; }

        public string SchemaHash { get; }

        public string BaseSourceHash { get; }

        public string SourceHash { get; }

        public string ArtifactHash { get; }

        public string ArtifactPath { get; }

        public string TargetScope { get; }

        public ConfigObjectNode ToNode()
        {
            return new ConfigObjectNode(new[]
            {
                new ConfigProperty("formatVersion", new ConfigIntegerNode(FormatVersion)),
                new ConfigProperty("configSetId", new ConfigStringNode(ConfigSetId)),
                new ConfigProperty("schemaId", new ConfigStringNode(SchemaId)),
                new ConfigProperty("schemaVersion", new ConfigIntegerNode(SchemaVersion)),
                new ConfigProperty("toolVersion", new ConfigStringNode(ToolVersion)),
                new ConfigProperty("schemaHash", new ConfigStringNode(SchemaHash)),
                new ConfigProperty("baseSourceHash", new ConfigStringNode(BaseSourceHash)),
                new ConfigProperty("sourceHash", new ConfigStringNode(SourceHash)),
                new ConfigProperty("artifactHash", new ConfigStringNode(ArtifactHash)),
                new ConfigProperty("artifactPath", new ConfigStringNode(ArtifactPath)),
                new ConfigProperty("targetScope", new ConfigStringNode(TargetScope))
            });
        }

        public static ConfigManifest Parse(byte[] utf8Json)
        {
            ConfigNode parsed = ConfigJsonParser.Parse(utf8Json);
            if (!(parsed is ConfigObjectNode root))
            {
                throw new FormatException("Config Manifest root must be an object.");
            }

            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "formatVersion",
                "configSetId",
                "schemaId",
                "schemaVersion",
                "toolVersion",
                "schemaHash",
                "baseSourceHash",
                "sourceHash",
                "artifactHash",
                "artifactPath",
                "targetScope"
            };
            foreach (ConfigProperty property in root.Properties)
            {
                if (!allowed.Remove(property.Name))
                {
                    throw new FormatException("Unknown or duplicate Manifest property '" + property.Name + "'.");
                }
            }

            if (allowed.Count != 0)
            {
                throw new FormatException("Manifest is missing required properties.");
            }

            int formatVersion = checked((int)ReadInteger(root, "formatVersion"));
            if (formatVersion != CurrentFormatVersion)
            {
                throw new NotSupportedException("Unsupported Manifest format version " + formatVersion + ".");
            }

            return new ConfigManifest(
                ReadString(root, "configSetId"),
                ReadString(root, "schemaId"),
                checked((int)ReadInteger(root, "schemaVersion")),
                ReadString(root, "toolVersion"),
                ReadString(root, "schemaHash"),
                ReadString(root, "baseSourceHash"),
                ReadString(root, "sourceHash"),
                ReadString(root, "artifactHash"),
                ReadString(root, "artifactPath"),
                ReadString(root, "targetScope"));
        }

        private static string ReadString(ConfigObjectNode root, string propertyName)
        {
            if (!root.TryGetValue(propertyName, out ConfigNode value) ||
                !(value is ConfigStringNode stringValue))
            {
                throw new FormatException("Manifest property '" + propertyName + "' must be a string.");
            }

            return stringValue.Value;
        }

        private static long ReadInteger(ConfigObjectNode root, string propertyName)
        {
            if (!root.TryGetValue(propertyName, out ConfigNode value) ||
                !(value is ConfigIntegerNode integerValue))
            {
                throw new FormatException("Manifest property '" + propertyName + "' must be an integer.");
            }

            return integerValue.Value;
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value is required.", parameterName);
            }

            return value;
        }

        private static string RequireHash(string value, string parameterName)
        {
            if (value == null || value.Length != 64)
            {
                throw new ArgumentException("SHA-256 hashes must contain 64 lowercase hex characters.", parameterName);
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException(
                        "SHA-256 hashes must contain 64 lowercase hex characters.",
                        parameterName);
                }
            }

            return value;
        }

        private static string RequireRelativePath(string value, string parameterName)
        {
            RequireText(value, parameterName);
            if (System.IO.Path.IsPathRooted(value) ||
                value.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                value.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(
                    "Artifact paths must be normalized relative paths without traversal.",
                    parameterName);
            }

            return value;
        }
    }
}
