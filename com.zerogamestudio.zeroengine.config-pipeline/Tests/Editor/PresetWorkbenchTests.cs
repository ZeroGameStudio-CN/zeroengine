using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using NUnit.Framework;
using ZeroGameStudio.ConfigPipeline.Editor;

namespace ZeroGameStudio.ConfigPipeline.Tests.Editor
{
    [TestFixture]
    [Category("ZGS.ConfigPipeline.CoreContract")]
    public sealed class PresetWorkbenchTests
    {
        private string root;
        private ConfigSchema schema;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "zgs-config-preset-workbench-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Config"));
            byte[] schemaBytes = Utf8(PresetSchemaJson);
            File.WriteAllBytes(Path.Combine(root, "Config", "schema.json"), schemaBytes);
            schema = ConfigSchemaParser.Parse(schemaBytes);
            File.WriteAllBytes(
                Path.Combine(root, "Config", "config-project.json"),
                Utf8(ProfileJson));
            WriteWorkbook(9, 6);
        }

        [TearDown]
        public void TearDown()
        {
            ConfigMaintenanceRegistry.ClearForTests();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void Plan_ExposesCanonicalEffectiveValuesAndFieldProvenance()
        {
            ConfigPipelinePreparedPlan plan = new ConfigPipelineService().Plan(
                root,
                "Config/config-project.json",
                "preset.sample",
                "package@1");

            ConfigEffectiveValue overridden = Find(plan, "$/instances/0/value");
            Assert.That(overridden.CanonicalValue, Is.EqualTo("9"));
            Assert.That(overridden.SourceKind, Is.EqualTo(ConfigValueSourceKind.Instance));
            Assert.That(overridden.Workbook, Is.EqualTo("Config/preset.xlsx"));
            Assert.That(overridden.Sheet, Is.EqualTo("Instances"));
            Assert.That(overridden.Row, Is.GreaterThan(0));
            Assert.That(overridden.Column, Is.GreaterThan(0));
            Assert.That(overridden.HasEditableInstanceCell, Is.True);

            ConfigEffectiveValue inherited = Find(plan, "$/instances/0/note");
            Assert.That(inherited.CanonicalValue, Is.EqualTo("\"preset-note\""));
            Assert.That(inherited.SourceKind, Is.EqualTo(ConfigValueSourceKind.Preset));
            Assert.That(inherited.SourceJsonPath, Is.EqualTo("$/presets/0/note"));
            Assert.That(inherited.Workbook, Is.EqualTo("Config/preset.xlsx"));
            Assert.That(inherited.Sheet, Is.EqualTo("Presets"));
            Assert.That(inherited.HasEditableInstanceCell, Is.False);
        }

        [Test]
        public void PresetReset_PreviewsDeterministicallyAndAppliesWorkbookAndArtifactsAtomically()
        {
            var service = new ConfigPipelineService();

            ConfigPresetResetPreview first = service.PlanPresetReset(
                root,
                "Config/config-project.json",
                "preset.sample",
                "package@1",
                "Generated/preset.json",
                "$/instances/0/value");
            ConfigPresetResetPreview second = service.PlanPresetReset(
                root,
                "Config/config-project.json",
                "preset.sample",
                "package@1",
                "Generated/preset.json",
                "$/instances/0/value");

            Assert.That(first.CurrentCanonicalValue, Is.EqualTo("9"));
            Assert.That(first.InheritedCanonicalValue, Is.EqualTo("7"));
            Assert.That(first.Workbook, Is.EqualTo("Config/preset.xlsx"));
            Assert.That(first.SourcePlanId, Is.EqualTo(second.SourcePlanId));
            Assert.That(first.ResetPlanId, Is.EqualTo(second.ResetPlanId));
            Assert.That(first.CandidateWorkbookHash, Is.EqualTo(second.CandidateWorkbookHash));
            Assert.That(first.CandidateWorkbookHash, Is.Not.EqualTo(first.SourceWorkbookHash));

            ConfigApplyResult result = service.ApplyExpectedPresetReset(
                root,
                "Config/config-project.json",
                "preset.sample",
                "package@1",
                first.TargetArtifactPath,
                first.JsonPath,
                first.SourcePlanId,
                first.ResetPlanId);

            Assert.That(result.PlanId, Is.EqualTo(first.ResetPlanId));
            Assert.That(result.ChangedFileCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(
                service.Check(
                    root,
                    "Config/config-project.json",
                    "preset.sample",
                    "package@1"),
                Is.True);
            ConfigEffectiveValue reset = Find(
                service.Plan(
                    root,
                    "Config/config-project.json",
                    "preset.sample",
                    "package@1"),
                "$/instances/0/value");
            Assert.That(reset.CanonicalValue, Is.EqualTo("7"));
            Assert.That(reset.SourceKind, Is.EqualTo(ConfigValueSourceKind.Preset));

            using (SpreadsheetDocument workbook = SpreadsheetDocument.Open(
                       Path.Combine(root, "Config", "preset.xlsx"),
                       false))
            {
                Assert.That(new OpenXmlValidator().Validate(workbook), Is.Empty);
            }
        }

        [Test]
        public void PresetReset_RejectsStalePreviewWithoutOverwritingNewSource()
        {
            var service = new ConfigPipelineService();
            ConfigPresetResetPreview preview = service.PlanPresetReset(
                root,
                "Config/config-project.json",
                "preset.sample",
                "package@1",
                "Generated/preset.json",
                "$/instances/0/value");
            WriteWorkbook(11, 6);

            ConfigPlanStaleException exception = Assert.Throws<ConfigPlanStaleException>(() =>
                service.ApplyExpectedPresetReset(
                    root,
                    "Config/config-project.json",
                    "preset.sample",
                    "package@1",
                    preview.TargetArtifactPath,
                    preview.JsonPath,
                    preview.SourcePlanId,
                    preview.ResetPlanId));

            Assert.That(exception.Message, Is.EqualTo("CONFIG_PLAN_CHANGED_REPLAN_REQUIRED"));
            Assert.That(
                Find(
                    service.Plan(
                        root,
                        "Config/config-project.json",
                        "preset.sample",
                        "package@1"),
                    "$/instances/0/value").CanonicalValue,
                Is.EqualTo("11"));
        }

        [Test]
        public void PresetReset_RejectsFieldThatFallsBackToSchemaDefault()
        {
            var service = new ConfigPipelineService();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                service.PlanPresetReset(
                    root,
                    "Config/config-project.json",
                    "preset.sample",
                    "package@1",
                    "Generated/preset.json",
                    "$/instances/0/localValue"));

            Assert.That(
                exception.Message,
                Is.EqualTo("CONFIG_RESET_TARGET_DOES_NOT_INHERIT_PRESET"));
            Assert.That(
                Find(
                    service.Plan(
                        root,
                        "Config/config-project.json",
                        "preset.sample",
                        "package@1"),
                    "$/instances/0/localValue").CanonicalValue,
                Is.EqualTo("6"));
        }

        private ConfigEffectiveValue Find(ConfigPipelinePreparedPlan plan, string jsonPath)
        {
            return plan.EffectiveValues.Single(value =>
                value.ArtifactPath == "Generated/preset.json" &&
                value.JsonPath == jsonPath);
        }

        private void WriteWorkbook(int instanceValue, int? localValue)
        {
            var preset = new ConfigObjectNode(new[]
            {
                new ConfigProperty("id", new ConfigStringNode("preset.standard")),
                new ConfigProperty("value", new ConfigIntegerNode(7)),
                new ConfigProperty("note", new ConfigStringNode("preset-note"))
            });
            var instanceProperties = new List<ConfigProperty>
            {
                new ConfigProperty("id", new ConfigStringNode("instance.first")),
                new ConfigProperty("presetId", new ConfigStringNode("preset.standard")),
                new ConfigProperty("value", new ConfigIntegerNode(instanceValue))
            };
            if (localValue.HasValue)
            {
                instanceProperties.Add(
                    new ConfigProperty("localValue", new ConfigIntegerNode(localValue.Value)));
            }

            var document = new ConfigDocument(
                "preset.sample",
                schema.SchemaId,
                schema.SchemaVersion,
                new ConfigObjectNode(new[]
                {
                    new ConfigProperty(
                        "presets",
                        new ConfigArrayNode(new ConfigNode[] { preset })),
                    new ConfigProperty(
                        "instances",
                        new ConfigArrayNode(new ConfigNode[]
                        {
                            new ConfigObjectNode(instanceProperties)
                        }))
                }));
            using (FileStream stream = File.Create(Path.Combine(root, "Config", "preset.xlsx")))
            {
                new XlsxConfigWorkbookWriter().WriteTemplate(
                    stream,
                    schema,
                    "preset.sample",
                    document,
                    null,
                    new[] { "presets", "instances" });
            }
        }

        private static byte[] Utf8(string value)
        {
            return new UTF8Encoding(false).GetBytes(value);
        }

        private const string ProfileJson =
            "{\"formatVersion\":1,\"configSets\":[{" +
            "\"configSetId\":\"preset.sample\",\"authoringSource\":\"excel\"," +
            "\"schema\":\"Config/schema.json\",\"workbooks\":[{" +
            "\"path\":\"Config/preset.xlsx\",\"tables\":[\"presets\",\"instances\"]}]," +
            "\"generatedNamespace\":\"Sample.Generated\"," +
            "\"rootClassName\":\"SampleConfig\"," +
            "\"codePath\":\"Generated/SampleConfig.g.cs\",\"targets\":[{" +
            "\"scope\":\"client\",\"json\":\"Generated/preset.json\"," +
            "\"manifest\":\"Generated/preset.manifest.json\"," +
            "\"sourceMap\":\"Generated/preset.sourcemap.json\"}]}]}";

        private const string PresetSchemaJson =
            "{\"$id\":\"urn:zgs:preset-workbench\",\"x-zgs-schema-version\":1," +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"presets\",\"instances\"],\"properties\":{" +
            "\"presets\":{\"type\":\"array\",\"x-zgs-sheet\":\"Presets\"," +
            "\"x-zgs-preset-type\":\"combat.rules\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"value\",\"note\"],\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"note\":{\"type\":\"string\"}}}}," +
            "\"instances\":{\"type\":\"array\",\"x-zgs-sheet\":\"Instances\"," +
            "\"x-zgs-preset-source\":\"#/properties/presets\"," +
            "\"x-zgs-preset-ref-field\":\"presetId\",\"items\":{" +
            "\"type\":\"object\",\"additionalProperties\":false," +
            "\"required\":[\"id\",\"presetId\",\"value\",\"note\",\"localValue\"]," +
            "\"properties\":{" +
            "\"id\":{\"type\":\"string\",\"x-zgs-primary-key\":true}," +
            "\"presetId\":{\"type\":\"string\",\"x-zgs-preset-type\":\"combat.rules\"," +
            "\"x-zgs-ref\":\"#/properties/presets/items/properties/id\"}," +
            "\"value\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"}," +
            "\"note\":{\"type\":\"string\"}," +
            "\"localValue\":{\"type\":\"integer\",\"x-zgs-number-type\":\"int32\"," +
            "\"default\":5}}}}}}";
    }
}
