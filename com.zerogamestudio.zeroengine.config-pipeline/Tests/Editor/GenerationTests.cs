using System.Linq;
using System.Text;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class GenerationTests
    {
        private const string SchemaJson =
            "{" +
            "\"$id\":\"zgs.sample.generation\"," +
            "\"x-zgs-schema-version\":2," +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"items\"]," +
            "\"properties\":{" +
            "\"items\":{" +
            "\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
            "\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"value\"]," +
            "\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"note\":{\"type\":\"string\",\"x-zgs-authoring-only\":true}," +
            "\"serverOnly\":{\"type\":\"string\",\"x-zgs-scope\":\"server\"}" +
            "}}}}}";

        [Test]
        public void Generate_IsDeterministicAndCodeContainsNoDataValues()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            ConfigDocument source = new ConfigDocument(
                "sample.generation",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("secret-data-id")),
                                new ConfigProperty("value", new ConfigIntegerNode(7)),
                                new ConfigProperty("note", new ConfigStringNode("author")),
                                new ConfigProperty("serverOnly", new ConfigStringNode("server"))
                            })
                        }))
                }));
            ConfigNormalizationResult normalized =
                ConfigSchemaNormalizer.Normalize(source, schema, "client");
            Assert.That(normalized.IsValid, Is.True);
            var options = Options();
            var generator = new ConfigArtifactGenerator(schema, options);

            var first = generator.Write(
                normalized.Document,
                new ConfigWriteContext("client", "Generated"));
            var second = generator.Write(
                normalized.Document,
                new ConfigWriteContext("client", "Generated"));

            Assert.That(first.Select(item => item.RelativePath),
                Is.EqualTo(second.Select(item => item.RelativePath)));
            for (int index = 0; index < first.Count; index++)
            {
                Assert.That(first[index].Content, Is.EqualTo(second[index].Content));
            }

            string code = Encoding.UTF8.GetString(
                first.Single(item => item.RelativePath.EndsWith(".g.cs")).Content);
            Assert.That(code, Does.Contain("SchemaVersion = 2"));
            Assert.That(code, Does.Not.Contain("secret-data-id"));
            Assert.That(code, Does.Not.Contain("= 7"));
        }

        [Test]
        public void WorkshopProjection_StripsExtensionsAuthoringAndServerFields()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            string projected = CanonicalJsonWriter.WriteText(
                WorkshopSchemaProjector.Project(schema, true));

            Assert.That(projected, Does.Not.Contain("x-zgs-"));
            Assert.That(projected, Does.Not.Contain("\"note\""));
            Assert.That(projected, Does.Not.Contain("\"serverOnly\""));
            Assert.That(projected, Does.Not.Contain("\"required\""));
            Assert.That(projected, Does.Contain("\"additionalProperties\": false"));
        }

        private static ConfigArtifactGenerationOptions Options()
        {
            return new ConfigArtifactGenerationOptions
            {
                ToolVersion = "1.0.0",
                TargetScope = "client",
                JsonPath = "Generated/sample.json",
                ManifestPath = "Generated/sample.manifest.json",
                SourceMapPath = "Generated/sample.source-map.json",
                CodePath = "Generated/SampleConfig.g.cs",
                WorkshopSchemaPath = "Generated/sample.workshop.schema.json",
                GeneratedNamespace = "Sample.Generated",
                RootClassName = "SampleConfigDto",
                ContractClassName = "SampleConfigContract",
                RelaxWorkshopRequired = true
            };
        }
    }
}
