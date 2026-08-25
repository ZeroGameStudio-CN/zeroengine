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

        private const string CompositeSchemaJson =
            "{" +
            "\"$id\":\"zgs.sample.composite-refs\"," +
            "\"x-zgs-schema-version\":1," +
            "\"type\":\"object\"," +
            "\"additionalProperties\":false," +
            "\"required\":[\"aliases\"]," +
            "\"properties\":{" +
            "\"aliases\":{" +
            "\"type\":\"array\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"domain\",\"kind\",\"legacyId\",\"canonicalId\"]," +
            "\"properties\":{" +
            "\"domain\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"kind\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"legacyId\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"canonicalId\":{\"type\":\"string\"}" +
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

        [Test]
        public void Validate_CompositePrimaryKey_AllowsRepeatedComponentsAndRejectsOnlyDuplicateTuple()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(CompositeSchemaJson));
            ConfigDocument document = CompositeDocument(includeDuplicateTuple: true);

            var diagnostics = new ConfigReferenceValidator(schema).Validate(
                document,
                new ConfigValidationContext("client"));

            ConfigDiagnostic[] duplicates = diagnostics
                .Where(diagnostic => diagnostic.Code == "CONFIG_PRIMARY_KEY_DUPLICATE")
                .ToArray();
            Assert.That(duplicates, Has.Length.EqualTo(1));
            Assert.That(duplicates[0].FieldPath, Is.EqualTo("$/aliases/2"));
            Assert.That(duplicates[0].Message, Does.Contain("domain='item'"));
            Assert.That(duplicates[0].Message, Does.Contain("legacyId='legacy.a'"));
        }

        [Test]
        public void Validate_ReferenceToCompositeComponent_IsRejectedAsAmbiguous()
        {
            const string schemaJson =
                "{\"$id\":\"zgs.sample.composite-ref-target\",\"x-zgs-schema-version\":1," +
                "\"type\":\"object\",\"additionalProperties\":false," +
                "\"required\":[\"aliases\",\"requests\"],\"properties\":{" +
                "\"aliases\":{\"type\":\"array\",\"items\":{\"type\":\"object\"," +
                "\"additionalProperties\":false,\"required\":[\"domain\",\"legacyId\"]," +
                "\"properties\":{" +
                "\"domain\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
                "\"legacyId\":{\"type\":\"string\",\"x-zgs-primary-key\":true}}}}," +
                "\"requests\":{\"type\":\"array\",\"items\":{\"type\":\"object\"," +
                "\"additionalProperties\":false,\"required\":[\"domain\"],\"properties\":{" +
                "\"domain\":{\"type\":\"string\",\"x-zgs-ref\":" +
                "\"#/properties/aliases/items/properties/domain\"}}}}}}";
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(schemaJson));
            var document = new ConfigDocument(
                "sample.composite-ref-target",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("aliases", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("domain", new ConfigStringNode("item")),
                            new ConfigProperty("legacyId", new ConfigStringNode("legacy.a"))
                        })
                    })),
                    new ConfigProperty("requests", new ConfigArrayNode(new ConfigNode[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("domain", new ConfigStringNode("item"))
                        })
                    }))
                }));

            var diagnostics = new ConfigReferenceValidator(schema).Validate(
                document,
                new ConfigValidationContext("client"));

            Assert.That(
                diagnostics.Select(diagnostic => diagnostic.Code),
                Does.Contain("CONFIG_REFERENCE_TARGET_INVALID"));
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

        private static ConfigDocument CompositeDocument(bool includeDuplicateTuple)
        {
            var rows = new System.Collections.Generic.List<ConfigNode>
            {
                CompositeAlias("item", "id", "legacy.a", "item.a"),
                CompositeAlias("item", "id", "legacy.b", "item.b")
            };
            if (includeDuplicateTuple)
            {
                rows.Add(CompositeAlias("item", "id", "legacy.a", "item.replacement"));
            }

            return new ConfigDocument(
                "sample.composite-refs",
                "zgs.sample.composite-refs",
                1,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("aliases", new ConfigArrayNode(rows))
                }));
        }

        private static ConfigObjectNode CompositeAlias(
            string domain,
            string kind,
            string legacyId,
            string canonicalId)
        {
            return new ConfigObjectNode(new[]
            {
                new ConfigProperty("domain", new ConfigStringNode(domain)),
                new ConfigProperty("kind", new ConfigStringNode(kind)),
                new ConfigProperty("legacyId", new ConfigStringNode(legacyId)),
                new ConfigProperty("canonicalId", new ConfigStringNode(canonicalId))
            });
        }
    }
}
