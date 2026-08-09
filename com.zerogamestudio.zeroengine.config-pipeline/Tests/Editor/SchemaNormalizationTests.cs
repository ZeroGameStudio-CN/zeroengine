using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class SchemaNormalizationTests
    {
        private const string SchemaJson =
            "{" +
            "\"$id\":\"zgs.sample.items\"," +
            "\"x-zgs-schema-version\":1," +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"items\"]," +
            "\"properties\":{" +
            "\"items\":{" +
            "\"type\":\"array\"," +
            "\"minItems\":1," +
            "\"uniqueItems\":true," +
            "\"items\":{" +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"id\",\"weight\"]," +
            "\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true,\"pattern\":\"^[a-z0-9._-]+$\"}," +
            "\"weight\":{\"type\":\"number\",\"x-zgs-number-type\":\"float32\",\"minimum\":0}," +
            "\"enabled\":{\"type\":\"boolean\",\"default\":true}," +
            "\"note\":{\"type\":\"string\",\"x-zgs-authoring-only\":true}," +
            "\"clientHint\":{\"type\":\"string\",\"x-zgs-scope\":\"client\"}," +
            "\"serverSecret\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\",\"x-zgs-scope\":\"server\"}" +
            "}}}}}";

        [Test]
        public void Normalize_MaterializesDefaultsAndProjectsScope()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            ConfigDocument source = Document(
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("item.a")),
                                new ConfigProperty("weight", new ConfigNumberNode(0.1d)),
                                new ConfigProperty("note", new ConfigStringNode("author only")),
                                new ConfigProperty("clientHint", new ConfigStringNode("visible")),
                                new ConfigProperty("serverSecret", new ConfigIntegerNode(7))
                            })
                        }))
                }));

            ConfigNormalizationResult result =
                ConfigSchemaNormalizer.Normalize(source, schema, "client");

            Assert.That(result.IsValid, Is.True, JoinDiagnostics(result));
            string json = CanonicalJsonWriter.WriteText(result.Document.Root);
            Assert.That(json, Does.Contain("\"enabled\": true"));
            Assert.That(json, Does.Contain("\"clientHint\": \"visible\""));
            Assert.That(json, Does.Not.Contain("note"));
            Assert.That(json, Does.Not.Contain("serverSecret"));
            Assert.That(json, Does.Contain("\"weight\": 0.1"));
        }

        [Test]
        public void Normalize_RejectsUnknownFieldsAndInvalidStableIds()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            ConfigDocument source = Document(
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode(" item.a ")),
                                new ConfigProperty("weight", new ConfigIntegerNode(1)),
                                new ConfigProperty("unknown", new ConfigBooleanNode(true))
                            })
                        }))
                }));

            ConfigNormalizationResult result =
                ConfigSchemaNormalizer.Normalize(source, schema, "client");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("CONFIG_UNKNOWN_PROPERTY"));
            Assert.That(result.Diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("CONFIG_STABLE_ID_INVALID"));
        }

        [Test]
        public void Parser_RejectsUnknownKeywordsAndOpenObjects()
        {
            string unknownKeyword = SchemaJson.Replace(
                "\"additionalProperties\":false,",
                "\"surprise\":true,\"additionalProperties\":false,");
            Assert.That(
                Assert.Throws<ConfigSchemaException>(
                    () => ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(unknownKeyword))).Code,
                Is.EqualTo("SCHEMA_KEYWORD_UNSUPPORTED"));

            string openObject = SchemaJson.Replace(
                "\"additionalProperties\":false,",
                "\"additionalProperties\":true,");
            Assert.That(
                Assert.Throws<ConfigSchemaException>(
                    () => ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(openObject))).Code,
                Is.EqualTo("SCHEMA_ADDITIONAL_PROPERTIES_REQUIRED"));
        }

        private static ConfigDocument Document(ConfigObjectNode root)
        {
            return new ConfigDocument("sample.items", "zgs.sample.items", 1, root);
        }

        private static string JoinDiagnostics(ConfigNormalizationResult result)
        {
            return string.Join(
                "\n",
                result.Diagnostics.Select(
                    diagnostic => diagnostic.Code + " " + diagnostic.FieldPath + " " + diagnostic.Message));
        }
    }
}
