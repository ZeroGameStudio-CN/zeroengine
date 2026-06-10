using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceGraphAssetValidatorTests
    {
        [Test]
        public void Validate_NullAsset_ReturnsGraphNullIssue()
        {
            var issues = TceGraphAssetValidator.Validate(null);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.NullGraph));
        }

        [Test]
        public void Validate_InvalidAsset_ReturnsRuntimeValidationIssues()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();

            var issues = TceGraphAssetValidator.Validate(asset);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.MissingTrigger));
            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.MissingEffect));
        }

        [Test]
        public void Validate_OldAssetVersion_ReturnsMigrationRequiredIssue()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();
            SetGraphSchemaVersion(asset, TceGraphSchema.LegacyUnversionedVersion);

            var issues = TceGraphAssetValidator.Validate(asset);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.GraphMigrationRequired));
        }

        [Test]
        public void Validate_FutureAssetVersion_ReturnsUnsupportedVersionIssue()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();
            SetGraphSchemaVersion(asset, TceGraphSchema.CurrentVersion + 1);

            var issues = TceGraphAssetValidator.Validate(asset);

            Assert.That(issues.Any(issue => issue.Code == TceValidationCodes.GraphVersionUnsupported));
        }

        [Test]
        public void MigrateToCurrent_OldAssetVersion_WritesCurrentVersion()
        {
            var asset = ScriptableObject.CreateInstance<TceGraphAsset>();
            SetGraphSchemaVersion(asset, TceGraphSchema.LegacyUnversionedVersion);

            bool migrated = TceGraphAssetMigration.MigrateToCurrent(asset);

            Assert.IsTrue(migrated);
            Assert.AreEqual(TceGraphSchema.CurrentVersion, asset.GraphSchemaVersion);
        }

        private static void SetGraphSchemaVersion(TceGraphAsset asset, int version)
        {
            var serializedObject = new SerializedObject(asset);
            SerializedProperty schemaVersion = serializedObject.FindProperty(TceGraphSerializedAccess.GraphSchemaVersionProperty);
            schemaVersion.intValue = version;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
