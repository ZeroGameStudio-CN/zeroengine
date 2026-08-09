using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphMigrationTests
    {
        [Test]
        public void Migrate_RenamesComponentIds()
        {
            TceExternalGraphDocument document = CreateLegacyDocument();
            document.Effects.Add(new TceExternalGraphNode("legacy.debug_log"));

            var registry = new TceGraphMigrationRegistry(new[]
            {
                new TceGraphMigrationStep(
                    TceGraphSchema.LegacyUnversionedVersion,
                    TceGraphSchema.CurrentVersion,
                    TceGraphMigrationStep.RenameComponent("legacy.debug_log", "zeroengine.tce.effect.debug_log"))
            });

            TceGraphMigrationResult result = registry.Migrate(document);

            AssertSucceeded(result);
            Assert.AreEqual(TceGraphSchema.CurrentVersion, result.Document.SchemaVersion);
            Assert.AreEqual("zeroengine.tce.effect.debug_log", result.Document.Effects[0].ComponentId);
        }

        [Test]
        public void Migrate_RenamesFields()
        {
            TceExternalGraphDocument document = CreateLegacyDocument();
            document.Effects.Add(new TceExternalGraphNode(
                "zeroengine.tce.effect.debug_log",
                new Dictionary<string, object> { ["Text"] = "accepted" }));

            var registry = new TceGraphMigrationRegistry(new[]
            {
                new TceGraphMigrationStep(
                    TceGraphSchema.LegacyUnversionedVersion,
                    TceGraphSchema.CurrentVersion,
                    TceGraphMigrationStep.RenameField("zeroengine.tce.effect.debug_log", "Text", "Message"))
            });

            TceGraphMigrationResult result = registry.Migrate(document);

            AssertSucceeded(result);
            Assert.IsFalse(result.Document.Effects[0].Fields.ContainsKey("Text"));
            Assert.AreEqual("accepted", result.Document.Effects[0].Fields["Message"]);
        }

        [Test]
        public void Migrate_AddsDefaultFieldValue()
        {
            TceExternalGraphDocument document = CreateLegacyDocument();
            document.Effects.Add(new TceExternalGraphNode("zeroengine.tce.effect.debug_log"));

            var registry = new TceGraphMigrationRegistry(new[]
            {
                new TceGraphMigrationStep(
                    TceGraphSchema.LegacyUnversionedVersion,
                    TceGraphSchema.CurrentVersion,
                    TceGraphMigrationStep.AddDefaultField(
                        "zeroengine.tce.effect.debug_log",
                        "Message",
                        "accepted"))
            });

            TceGraphMigrationResult result = registry.Migrate(document);

            AssertSucceeded(result);
            Assert.AreEqual("accepted", result.Document.Effects[0].Fields["Message"]);
        }

        [Test]
        public void Migrate_RemovedComponent_ReturnsBlockingIssue()
        {
            TceExternalGraphDocument document = CreateLegacyDocument();
            document.Effects.Add(new TceExternalGraphNode("legacy.removed_effect"));

            var registry = new TceGraphMigrationRegistry(new[]
            {
                new TceGraphMigrationStep(
                    TceGraphSchema.LegacyUnversionedVersion,
                    TceGraphSchema.CurrentVersion,
                    TceGraphMigrationStep.FailRemovedComponent(
                        "legacy.removed_effect",
                        "legacy.removed_effect has no safe replacement."))
            });

            TceGraphMigrationResult result = registry.Migrate(document);

            Assert.IsFalse(result.Succeeded);
            Assert.That(result.Issues.Any(issue => issue.Code == TceValidationCodes.GraphMigrationFailed));
            Assert.AreEqual(TceGraphSchema.LegacyUnversionedVersion, document.SchemaVersion);
        }

        [Test]
        public void Migrate_UnsupportedFutureVersion_ReturnsIssue()
        {
            TceExternalGraphDocument document = CreateLegacyDocument();
            document.SchemaVersion = TceGraphSchema.CurrentVersion + 1;

            var registry = new TceGraphMigrationRegistry();

            TceGraphMigrationResult result = registry.Migrate(document);

            Assert.IsFalse(result.Succeeded);
            Assert.That(result.Issues.Any(issue => issue.Code == TceValidationCodes.GraphVersionUnsupported));
        }

        private static TceExternalGraphDocument CreateLegacyDocument()
        {
            return new TceExternalGraphDocument
            {
                Format = TceGraphSchema.Format,
                SchemaVersion = TceGraphSchema.LegacyUnversionedVersion,
                GraphId = "legacy_graph",
                DisplayName = "Legacy Graph"
            };
        }

        private static void AssertSucceeded(TceGraphMigrationResult result)
        {
            Assert.IsTrue(result.Succeeded, string.Join("\n", result.Issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
        }
    }
}
