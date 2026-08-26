using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests
{
    [TestFixture]
    [Category("Unit")]
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class PresetResolutionTests
    {
        [Test]
        public void Resolve_SingleTypedPreset_FlattensValuesAndTracksFieldSources()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(PresetSchemaJson));
            ConfigDocument source = PresetDocument(includeInheritedCollectionRows: false);
            var rawMap = new[]
            {
                Source("$/presets/0/value", "Presets", 3, 2),
                Source("$/presets/0/note", "Presets", 3, 3),
                Source("$/presets/0/entries/0/amount", "PresetEntries", 3, 4),
                Source("$/instances/0/value", "Instances", 3, 3),
                Source("$/instances/0/entriesOverrideMode", "Instances", 3, 5),
                Source("$/instances/1/note", "Instances", 4, 4),
                Source("$/instances/1/entriesOverrideMode", "Instances", 4, 5)
            };

            ConfigPresetResolutionResult resolved = ConfigPresetResolver.Resolve(source, schema, rawMap);

            Assert.That(resolved.IsValid, Is.True, JoinDiagnostics(resolved.Diagnostics));
            ConfigNormalizationResult normalized = ConfigSchemaNormalizer.Normalize(
                resolved.Document,
                schema,
                "client");
            Assert.That(normalized.IsValid, Is.True, JoinDiagnostics(normalized.Diagnostics));

            ConfigArrayNode instances = ReadArray(normalized.Document.Root, "instances");
            ConfigObjectNode first = (ConfigObjectNode)instances.Items[0];
            ConfigObjectNode second = (ConfigObjectNode)instances.Items[1];
            Assert.That(ReadInteger(first, "value"), Is.EqualTo(0));
            Assert.That(ReadString(first, "note"), Is.EqualTo("preset-note"));
            Assert.That(ReadInteger(first, "schemaOnly"), Is.EqualTo(5));
            Assert.That(ReadArray(first, "entries").Items, Has.Count.EqualTo(1));
            Assert.That(ReadInteger(second, "value"), Is.EqualTo(10));
            Assert.That(second.TryGetValue("note", out ConfigNode cleared), Is.True);
            Assert.That(cleared, Is.TypeOf<ConfigNullNode>());
            Assert.That(ReadArray(second, "entries").Items, Is.Empty);

            IReadOnlyList<XlsxSourceMapEntry> finalMap = ConfigSourceMapBuilder.Build(
                normalized.Document,
                schema,
                resolved.SourceMap);
            AssertSource(finalMap, "$/instances/0/value", ConfigValueSourceKind.Instance);
            AssertSource(finalMap, "$/instances/0/note", ConfigValueSourceKind.Preset);
            AssertSource(finalMap, "$/instances/0/schemaOnly", ConfigValueSourceKind.Schema);
            AssertSource(finalMap, "$/instances/0/entries/0/amount", ConfigValueSourceKind.Preset);
            AssertSource(finalMap, "$/instances/1/note", ConfigValueSourceKind.Instance);
            AssertSource(finalMap, "$/instances/1/entries", ConfigValueSourceKind.Instance);

            var generator = new ConfigArtifactGenerator(
                schema,
                new ConfigArtifactGenerationOptions
                {
                    ToolVersion = "2.2.0",
                    TargetScope = "client",
                    JsonPath = "Generated/config.json",
                    ManifestPath = "Generated/config.manifest.json",
                    SourceMapPath = "Generated/config.sourcemap.json"
                },
                resolved.SourceMap);
            string sourceMapJson = Encoding.UTF8.GetString(generator.Write(
                    normalized.Document,
                    new ConfigWriteContext("client", "Generated"))
                .Single(value => value.RelativePath.EndsWith("sourcemap.json", StringComparison.Ordinal))
                .Content);
            Assert.That(sourceMapJson, Does.Contain("\"formatVersion\": 2"));
            Assert.That(sourceMapJson, Does.Contain("\"sourceKind\": \"Schema\""));
            Assert.That(sourceMapJson, Does.Contain("\"sourceKind\": \"Preset\""));
            Assert.That(sourceMapJson, Does.Contain("\"sourceKind\": \"Instance\""));
        }

        [Test]
        public void Resolve_InheritWithInstanceRows_RejectsImplicitCollectionMerge()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(PresetSchemaJson));

            ConfigPresetResolutionResult result = ConfigPresetResolver.Resolve(
                PresetDocument(includeInheritedCollectionRows: true),
                schema,
                Array.Empty<XlsxSourceMapEntry>());

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Diagnostics.Select(value => value.Code),
                Does.Contain("CONFIG_PRESET_COLLECTION_REPLACE_REQUIRED"));
        }

        [Test]
        public void Workbook_RoundTripsReservedEmptyClearAndEscapedAtTokens()
        {
            ConfigSchema schema = ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(TokenSchemaJson));
            var row = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("row.1")),
                new ConfigProperty("cleared", ConfigNullNode.Instance),
                new ConfigProperty("empty", new ConfigStringNode(string.Empty)),
                new ConfigProperty("literal", new ConfigStringNode("@empty"))
            });
            var source = new ConfigDocument(
                "tokens",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("items", new ConfigArrayNode(new[] { row }))
                }));

            using (var stream = new MemoryStream())
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(stream, schema, "tokens", source);
                stream.Position = 0;
                ConfigDocument roundTrip = new XlsxConfigSourceReader(schema).Read(
                    stream,
                    new ConfigReadContext("tokens", schema.SchemaId, schema.SchemaVersion));
                ConfigObjectNode actual = (ConfigObjectNode)ReadArray(roundTrip.Root, "items").Items[0];
                Assert.That(actual.TryGetValue("cleared", out ConfigNode cleared), Is.True);
                Assert.That(cleared, Is.TypeOf<ConfigNullNode>());
                Assert.That(ReadString(actual, "empty"), Is.Empty);
                Assert.That(ReadString(actual, "literal"), Is.EqualTo("@empty"));
            }
        }

        [Test]
        public void Schema_TypedPresetReferenceWithMismatchedType_IsRejected()
        {
            string invalid = PresetSchemaJson.Replace(
                "\"x-zgs-preset-type\":\"combat.rules\",\"x-zgs-ref\":\"#/properties/presets/items/properties/id\"",
                "\"x-zgs-preset-type\":\"combat.other\",\"x-zgs-ref\":\"#/properties/presets/items/properties/id\"");

            ConfigSchemaException exception = Assert.Throws<ConfigSchemaException>(() =>
                ConfigSchemaParser.Parse(Encoding.UTF8.GetBytes(invalid)));

            Assert.That(exception.Code, Is.EqualTo("SCHEMA_PRESET_REFERENCE_INVALID"));
        }

        private static ConfigDocument PresetDocument(bool includeInheritedCollectionRows)
        {
            var presetEntry = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("preset.entry")),
                new ConfigProperty("order", new ConfigIntegerNode(0)),
                new ConfigProperty("amount", new ConfigIntegerNode(3))
            });
            var preset = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("preset.standard")),
                new ConfigProperty("value", new ConfigIntegerNode(10)),
                new ConfigProperty("note", new ConfigStringNode("preset-note")),
                new ConfigProperty("entries", new ConfigArrayNode(new[] { presetEntry }))
            });
            var firstProperties = new List<ConfigProperty>
            {
                new ConfigProperty("id", new ConfigStringNode("instance.first")),
                new ConfigProperty("presetId", new ConfigStringNode("preset.standard")),
                new ConfigProperty("value", new ConfigIntegerNode(0)),
                new ConfigProperty("entriesOverrideMode", new ConfigStringNode("Inherit"))
            };
            if (includeInheritedCollectionRows)
            {
                firstProperties.Add(new ConfigProperty(
                    "entries",
                    new ConfigArrayNode(new[]
                    {
                        new ConfigObjectNode(new[]
                        {
                            new ConfigProperty("id", new ConfigStringNode("instance.entry")),
                            new ConfigProperty("order", new ConfigIntegerNode(0)),
                            new ConfigProperty("amount", new ConfigIntegerNode(9))
                        })
                    })));
            }
            else
            {
                firstProperties.Add(new ConfigProperty(
                    "entries",
                    new ConfigArrayNode(Array.Empty<ConfigNode>())));
            }

            var second = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("instance.second")),
                new ConfigProperty("presetId", new ConfigStringNode("preset.standard")),
                new ConfigProperty("note", ConfigNullNode.Instance),
                new ConfigProperty("entriesOverrideMode", new ConfigStringNode("Replace")),
                new ConfigProperty("entries", new ConfigArrayNode(Array.Empty<ConfigNode>()))
            });
            return new ConfigDocument(
                "preset.sample",
                "urn:zgs:preset-sample",
                1,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty("presets", new ConfigArrayNode(new[] { preset })),
                    new ConfigProperty(
                        "instances",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(firstProperties),
                            second
                        }))
                }));
        }

        private static XlsxSourceMapEntry Source(string path, string sheet, int row, int column)
        {
            return new XlsxSourceMapEntry(path, "preset.xlsx", sheet, row, column);
        }

        private static void AssertSource(
            IEnumerable<XlsxSourceMapEntry> sourceMap,
            string path,
            ConfigValueSourceKind expected)
        {
            XlsxSourceMapEntry entry = sourceMap.Single(value => value.JsonPath == path);
            Assert.That(entry.SourceKind, Is.EqualTo(expected), path);
            Assert.That(entry.SchemaPath, Is.Not.Empty, path);
        }

        private static ConfigArrayNode ReadArray(ConfigObjectNode source, string name)
        {
            Assert.That(source.TryGetValue(name, out ConfigNode value), Is.True, name);
            return (ConfigArrayNode)value;
        }

        private static long ReadInteger(ConfigObjectNode source, string name)
        {
            Assert.That(source.TryGetValue(name, out ConfigNode value), Is.True, name);
            return ((ConfigIntegerNode)value).Value;
        }

        private static string ReadString(ConfigObjectNode source, string name)
        {
            Assert.That(source.TryGetValue(name, out ConfigNode value), Is.True, name);
            return ((ConfigStringNode)value).Value;
        }

        private static string JoinDiagnostics(IEnumerable<ConfigDiagnostic> diagnostics)
        {
            return string.Join("\n", diagnostics.Select(value => value.Code + ": " + value.Message));
        }

        private const string TokenSchemaJson =
            "{\"$id\":\"urn:zgs:tokens\",\"x-zgs-schema-version\":1," +
            "\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"items\"]," +
            "\"properties\":{\"items\":{\"type\":\"array\",\"x-zgs-sheet\":\"Items\"," +
            "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"cleared\",\"empty\",\"literal\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"cleared\":{\"type\":\"string\",\"x-zgs-nullable\":true}," +
            "\"empty\":{\"type\":\"string\"},\"literal\":{\"type\":\"string\"}}}}}}";

        private const string PresetSchemaJson =
            "{\"$id\":\"urn:zgs:preset-sample\",\"x-zgs-schema-version\":1," +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"presets\",\"instances\"],\"properties\":{" +
            "\"presets\":{\"type\":\"array\",\"x-zgs-sheet\":\"Presets\"," +
            "\"x-zgs-preset-type\":\"combat.rules\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"value\",\"note\",\"entries\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"note\":{\"type\":\"string\",\"x-zgs-nullable\":true}," +
            "\"entries\":{\"type\":\"array\",\"x-zgs-sheet\":\"PresetEntries\"," +
            "\"x-zgs-parent-key\":\"presetId\",\"x-zgs-order-field\":\"order\"," +
            "\"items\":{\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"order\",\"amount\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"order\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"amount\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}}}}}}}," +
            "\"instances\":{\"type\":\"array\",\"x-zgs-sheet\":\"Instances\"," +
            "\"x-zgs-preset-source\":\"#/properties/presets\"," +
            "\"x-zgs-preset-ref-field\":\"presetId\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"presetId\",\"value\",\"note\",\"schemaOnly\"," +
            "\"entriesOverrideMode\",\"entries\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"presetId\":{\"type\":\"string\",\"x-zgs-preset-type\":\"combat.rules\"," +
            "\"x-zgs-ref\":\"#/properties/presets/items/properties/id\"}," +
            "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"note\":{\"type\":\"string\",\"x-zgs-nullable\":true}," +
            "\"schemaOnly\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\",\"default\":5}," +
            "\"entriesOverrideMode\":{\"type\":\"string\",\"enum\":[\"Inherit\",\"Replace\"]}," +
            "\"entries\":{\"type\":\"array\",\"x-zgs-sheet\":\"InstanceEntries\"," +
            "\"x-zgs-parent-key\":\"instanceId\",\"x-zgs-order-field\":\"order\"," +
            "\"x-zgs-override-mode-field\":\"entriesOverrideMode\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"order\",\"amount\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"order\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"amount\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}}}}}}}}}";
    }
}
