using System;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline
{
    public sealed class ConfigArtifactReader
    {
        public ConfigDocument Read(
            byte[] artifactJson,
            byte[] manifestJson,
            ConfigArtifactContract expectedContract)
        {
            if (artifactJson == null)
            {
                throw new ArgumentNullException(nameof(artifactJson));
            }

            if (manifestJson == null)
            {
                throw new ArgumentNullException(nameof(manifestJson));
            }

            if (expectedContract == null)
            {
                throw new ArgumentNullException(nameof(expectedContract));
            }

            ConfigManifest manifest = ConfigManifest.Parse(manifestJson);
            RequireEqual("configSetId", expectedContract.ConfigSetId, manifest.ConfigSetId);
            RequireEqual("schemaId", expectedContract.SchemaId, manifest.SchemaId);
            if (expectedContract.SchemaVersion != manifest.SchemaVersion)
            {
                throw new ConfigArtifactException(
                    "CONFIG_SCHEMA_VERSION_MISMATCH",
                    "Expected schema version " + expectedContract.SchemaVersion +
                    ", got " + manifest.SchemaVersion + ".");
            }

            RequireEqual("schemaHash", expectedContract.SchemaHash, manifest.SchemaHash);
            string actualArtifactHash = ConfigHash.Sha256(artifactJson);
            RequireEqual("artifactHash", manifest.ArtifactHash, actualArtifactHash);

            ConfigNode parsed = ConfigJsonParser.Parse(artifactJson);
            if (!(parsed is ConfigObjectNode root))
            {
                throw new ConfigArtifactException(
                    "CONFIG_ARTIFACT_ROOT_INVALID",
                    "Config artifact root must be an object.");
            }

            byte[] canonical = CanonicalJsonWriter.WriteUtf8(root);
            if (!BytesEqual(artifactJson, canonical))
            {
                throw new ConfigArtifactException(
                    "CONFIG_ARTIFACT_NOT_CANONICAL",
                    "Config artifact is valid JSON but is not canonical.");
            }

            return new ConfigDocument(
                manifest.ConfigSetId,
                manifest.SchemaId,
                manifest.SchemaVersion,
                root);
        }

        private static void RequireEqual(string field, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new ConfigArtifactException(
                    "CONFIG_" + field.ToUpperInvariant() + "_MISMATCH",
                    "Expected " + field + " '" + expected + "', got '" + actual + "'.");
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class ConfigArtifactException : Exception
    {
        public ConfigArtifactException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
