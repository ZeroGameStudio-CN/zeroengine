using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class ReferenceValidatorTests
    {
        private const string SchemaJson =
            "{" +
            "\"$id\":\"zgs.sample.refs\"," +
            "\"x-zgs-schema-version\":1," +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"categories\",\"items\"]," +
            "\"properties\":{" +
            "\"categories\":{" +
            "\"type\":\"array\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\"]," +
            "\"properties\":{\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}," +
            "\"items\":{" +
            "\"type\":\"array\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"id\",\"categoryId\"]," +
            "\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"categoryId\":{\"type\":\"string\",\"x-zgs-ref\":\"#/properties/categories/items/properties/id\"}" +
            "}}}}}";

        [Test]
        public void Validate_AcceptsExistingReference()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            ConfigDocument document = Document("category.a");

            var diagnostics = new ConfigReferenceValidator(schema).Validate(
                document,
                new ConfigValidationContext("client"));

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void Validate_RejectsDanglingReference()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(SchemaJson));
            ConfigDocument document = Document("category.missing");

            var diagnostics = new ConfigReferenceValidator(schema).Validate(
                document,
                new ConfigValidationContext("client"));

            Assert.That(
                diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("CONFIG_REFERENCE_DANGLING"));
        }

        private static ConfigDocument Document(string categoryReference)
        {
            return new ConfigDocument(
                "sample.refs",
                "zgs.sample.refs",
                1,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "categories",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("category.a"))
                            })
                        })),
                    new ConfigProperty(
                        "items",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(new[]
                            {
                                new ConfigProperty("id", new ConfigStringNode("item.a")),
                                new ConfigProperty(
                                    "categoryId",
                                    new ConfigStringNode(categoryReference))
                            })
                        }))
                }));
        }
    }
}
