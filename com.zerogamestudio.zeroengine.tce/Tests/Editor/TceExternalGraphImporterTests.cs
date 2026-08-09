using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceExternalGraphImporterTests
    {
        [Test]
        public void DefaultRegistry_ContainsBuiltInComponentIds()
        {
            TceComponentRegistry registry = TceComponentRegistry.CreateDefault();

            Assert.IsTrue(registry.TryGet("zeroengine.tce.effect.debug_log", out TceComponentRegistryEntry entry));
            Assert.AreEqual(typeof(DebugLogEffectData), entry.DataType);
            Assert.AreEqual(TceComponentDocCategory.Effect, entry.Category);
        }

        [Test]
        public void ExplicitRegistry_RejectsUnlistedComponents()
        {
            TceComponentRegistry registry = TceComponentRegistry.Create(typeof(DebugLogEffectData));

            Assert.IsFalse(registry.TryGet("zeroengine.tce.trigger.on_install", out _));
        }

        [Test]
        public void Registry_DoesNotResolveClrTypeNames()
        {
            TceComponentRegistry registry = TceComponentRegistry.CreateDefault();

            Assert.IsFalse(registry.TryGet(typeof(DebugLogEffectData).FullName, out _));
        }

        [Test]
        public void Import_ValidDocument_CreatesRuntimeGraph()
        {
            TceExternalGraphDocument document = CreateValidDocument();

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertSucceeded(result);
            Assert.IsInstanceOf<OnInstallTriggerData>(result.Graph.Triggers[0]);
            Assert.AreEqual("accepted", ((DebugLogEffectData)result.Graph.Effects[0]).Message);
        }

        [Test]
        public void Import_UnknownComponent_ReturnsUnsupportedComponentIssue()
        {
            TceExternalGraphDocument document = CreateValidDocument();
            document.Effects.Add(new TceExternalGraphNode("ZeroEngine.TCE.DebugLogEffectData"));

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertFailedWith(result, TceValidationCodes.UnsupportedComponent);
        }

        [Test]
        public void Import_UnknownField_ReturnsInvalidFieldIssue()
        {
            TceExternalGraphDocument document = CreateValidDocument();
            document.Effects[0].Fields.Add("UnknownField", "value");

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertFailedWith(result, TceValidationCodes.InvalidField);
        }

        [Test]
        public void Import_InvalidEnumValue_ReturnsInvalidEnumIssue()
        {
            TceExternalGraphDocument document = CreateValidDocument();
            document.Effects[0].Fields.Add("Target", "NotATarget");

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertFailedWith(result, TceValidationCodes.InvalidEnumValue);
        }

        [Test]
        public void Import_ComponentInWrongLane_ReturnsRuntimeTypeMismatchIssue()
        {
            TceExternalGraphDocument document = CreateValidDocument();
            document.Effects.Add(new TceExternalGraphNode("zeroengine.tce.trigger.on_install"));

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertFailedWith(result, TceValidationCodes.RuntimeTypeMismatch);
        }

        [Test]
        public void Import_OldVersionWithoutMigration_ReturnsMigrationRequiredIssue()
        {
            TceExternalGraphDocument document = CreateValidDocument();
            document.SchemaVersion = TceGraphSchema.LegacyUnversionedVersion;

            TceExternalGraphImportResult result = TceExternalGraphImporter.Import(document, TceComponentRegistry.CreateDefault());

            AssertFailedWith(result, TceValidationCodes.GraphMigrationRequired);
        }

        private static TceExternalGraphDocument CreateValidDocument()
        {
            var document = new TceExternalGraphDocument
            {
                Format = TceGraphSchema.Format,
                SchemaVersion = TceGraphSchema.CurrentVersion,
                GraphId = "valid_graph",
                DisplayName = "Valid Graph"
            };

            document.Triggers.Add(new TceExternalGraphNode("zeroengine.tce.trigger.on_install"));
            document.Effects.Add(new TceExternalGraphNode(
                "zeroengine.tce.effect.debug_log",
                new Dictionary<string, object> { ["Message"] = "accepted" }));
            return document;
        }

        private static void AssertSucceeded(TceExternalGraphImportResult result)
        {
            Assert.IsTrue(result.Succeeded, string.Join("\n", result.Issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
        }

        private static void AssertFailedWith(TceExternalGraphImportResult result, string code)
        {
            Assert.IsFalse(result.Succeeded);
            Assert.That(result.Issues.Any(issue => issue.Code == code));
        }
    }
}
