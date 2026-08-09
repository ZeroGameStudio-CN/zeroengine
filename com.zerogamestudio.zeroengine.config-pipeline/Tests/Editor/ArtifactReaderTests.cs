using System.Text;
using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class ArtifactReaderTests
    {
        [Test]
        public void Read_RequiresMatchingContractHashAndCanonicalArtifact()
        {
            var root = new ConfigObjectNode(new[]
            {
                new ConfigProperty("value", new ConfigIntegerNode(3))
            });
            byte[] artifact = CanonicalJsonWriter.WriteUtf8(root);
            string artifactHash = ConfigHash.Sha256(artifact);
            const string schemaHash =
                "1111111111111111111111111111111111111111111111111111111111111111";
            var manifest = new ConfigManifest(
                "sample",
                "zgs.sample",
                1,
                "1.0.0",
                schemaHash,
                ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(root)),
                ConfigHash.Sha256(CanonicalJsonWriter.WriteUtf8(root)),
                artifactHash,
                "sample.json",
                "client");
            byte[] manifestBytes = CanonicalJsonWriter.WriteUtf8(manifest.ToNode());

            ConfigDocument document = new ConfigArtifactReader().Read(
                artifact,
                manifestBytes,
                new ConfigArtifactContract("sample", "zgs.sample", 1, schemaHash));

            Assert.That(document.ConfigSetId, Is.EqualTo("sample"));

            byte[] tampered = Encoding.UTF8.GetBytes("{\"value\":3}\n");
            ConfigArtifactException exception = Assert.Throws<ConfigArtifactException>(
                () => new ConfigArtifactReader().Read(
                    tampered,
                    manifestBytes,
                    new ConfigArtifactContract("sample", "zgs.sample", 1, schemaHash)));
            Assert.That(exception.Code, Is.EqualTo("CONFIG_ARTIFACTHASH_MISMATCH"));
        }
    }
}
