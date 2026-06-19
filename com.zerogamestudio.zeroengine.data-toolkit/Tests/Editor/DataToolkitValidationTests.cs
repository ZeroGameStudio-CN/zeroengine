using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataToolkitValidationTests
    {
        [Test]
        public void CreateAssetMenuTypesAreManageable()
        {
            Assert.IsTrue(ManageableDataTypeDiscovery.IsManageableScriptableObjectType(typeof(MenuBackedConfigAsset)));
        }

        [Test]
        public void ZeroEngineStyleConfigNamesAreManageable()
        {
            Assert.IsTrue(ManageableDataTypeDiscovery.IsManageableScriptableObjectType(typeof(SuffixConfigSO)));
        }

        [Test]
        public void EmptyStableIdProducesWarning()
        {
            var asset = ScriptableObject.CreateInstance<SuffixConfigSO>();
            try
            {
                asset.name = "MissingId";

                var report = DataAssetValidationService.ValidateAsset(asset, "Assets/MissingId.asset");

                Assert.AreEqual(0, report.ErrorCount);
                Assert.AreEqual(1, report.WarningCount);
                Assert.That(report.Issues.Single().FieldPath, Is.EqualTo("id"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DuplicateStableIdsProduceErrors()
        {
            var first = ScriptableObject.CreateInstance<SuffixConfigSO>();
            var second = ScriptableObject.CreateInstance<SuffixConfigSO>();
            try
            {
                first.name = "First";
                first.SetId("shared");
                second.name = "Second";
                second.SetId("shared");

                var report = DataAssetValidationService.ValidateLoadedObjects(new[] { first, second });

                Assert.AreEqual(2, report.ErrorCount);
                Assert.That(report.Issues.All(issue => issue.Message.Contains("duplicated")));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void CsvEscapesCommasQuotesAndNewlines()
        {
            Assert.AreEqual("\"a,b\"\"c\n\"", DataAssetCsvUtility.Escape("a,b\"c\n"));
        }

        [Test]
        public void CustomValidationProvidersAddIssuesToReport()
        {
            var asset = ScriptableObject.CreateInstance<SuffixConfigSO>();
            try
            {
                asset.name = "NeedsPackageRule";
                asset.SetId("custom-rule");

                var report = DataAssetValidationService.ValidateLoadedObjects(
                    new[] { asset },
                    new[] { new RequiresDisplayNameValidator() });

                Assert.AreEqual(1, report.ErrorCount);
                Assert.That(report.Issues.Single().Message, Does.Contain("Display name"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void DefaultProfileDiscoversValidationProviders()
        {
            var profile = DataToolkitProjectRegistry.CreateDefaultProfile();

            Assert.IsTrue(profile.ValidationProviders.Any(provider => provider.GetType() == typeof(DiscoveredValidationProvider)));
        }

        [Test]
        public void DefaultProfileAdaptsPackageConfigValidators()
        {
            var profile = DataToolkitProjectRegistry.CreateDefaultProfile();
            var provider = profile.ValidationProviders.FirstOrDefault(candidate => candidate.GetType().Name == "PackageConfigValidationProvider");
            Assert.NotNull(provider);

            var asset = ScriptableObject.CreateInstance<PackageBridgeConfigSO>();
            try
            {
                asset.name = "BridgeConfig";

                var report = DataAssetValidationService.ValidateAsset(
                    asset,
                    "Assets/BridgeConfig.asset",
                    new[] { provider });

                var issue = report.Issues.Single(candidate => candidate.FieldPath == "BridgeField");
                Assert.AreEqual(DataValidationSeverity.Error, issue.Severity);
                Assert.AreEqual(nameof(PackageBridgeConfigSO), issue.TypeName);
                Assert.AreEqual("BridgeConfig", issue.AssetName);
                Assert.AreEqual("Assets/BridgeConfig.asset", issue.AssetPath);
                Assert.That(issue.Message, Does.Contain("package bridge"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void GenericSemanticValidationFindsDesignerConfigIssues()
        {
            var asset = ScriptableObject.CreateInstance<DesignerConfigSO>();
            try
            {
                asset.name = "DesignerConfig";
                asset.DisplayName = " ";
                asset.Cost = -1;
                asset.DropChance = 1.5f;
                asset.MinLevel = 10;
                asset.MaxLevel = 5;
                asset.Rewards.Add(null);

                var report = DataAssetValidationService.ValidateAsset(asset, "Assets/DesignerConfig.asset");

                Assert.AreEqual(3, report.ErrorCount);
                Assert.AreEqual(2, report.WarningCount);
                Assert.That(report.Issues.Any(issue => issue.FieldPath == "DisplayName" && issue.Message.Contains("display text")));
                Assert.That(report.Issues.Any(issue => issue.FieldPath == "Cost" && issue.Message.Contains("negative")));
                Assert.That(report.Issues.Any(issue => issue.FieldPath == "DropChance" && issue.Message.Contains("between 0 and 1")));
                Assert.That(report.Issues.Any(issue => issue.FieldPath == "MaxLevel" && issue.Message.Contains("lower than min")));
                Assert.That(report.Issues.Any(issue => issue.FieldPath == "Rewards.Array.data[0]" && issue.Message.Contains("empty entry")));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CsvImportPreviewAppliesSupportedFieldChanges()
        {
            var asset = ScriptableObject.CreateInstance<ImportableConfigSO>();
            try
            {
                asset.name = "Config";
                asset.SetValues("old", 1, false);
                var assetsByPath = new Dictionary<string, Object>
                {
                    ["Assets/Config.asset"] = asset
                };
                const string csv =
                    "Path,Field,Value\n" +
                    "Assets/Config.asset,id,new_id\n" +
                    "Assets/Config.asset,cost,25\n" +
                    "Assets/Config.asset,enabled,true\n";

                var preview = DataAssetCsvImporter.PreviewFieldUpdatesFromCsv(csv, assetsByPath);

                Assert.AreEqual(3, preview.PendingCount);

                var applied = DataAssetFieldChangeService.Apply(preview, "Test CSV Import");

                Assert.AreEqual(3, applied);
                Assert.AreEqual("new_id", asset.Id);
                Assert.AreEqual(25, asset.Cost);
                Assert.IsTrue(asset.Enabled);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CsvImportPreviewDoesNotModifyAssetBeforeApply()
        {
            var asset = ScriptableObject.CreateInstance<ImportableConfigSO>();
            try
            {
                asset.name = "Config";
                asset.SetValues("old", 1, false);
                var assetsByPath = new Dictionary<string, Object>
                {
                    ["Assets/Config.asset"] = asset
                };
                const string csv = "Path,Field,Value\nAssets/Config.asset,cost,25\n";

                var preview = DataAssetCsvImporter.PreviewFieldUpdatesFromCsv(csv, assetsByPath);

                Assert.AreEqual(1, preview.PendingCount);
                Assert.AreEqual(1, asset.Cost);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void BatchEditPreviewReportsMissingField()
        {
            var asset = ScriptableObject.CreateInstance<ImportableConfigSO>();
            try
            {
                asset.name = "BatchConfig";

                var preview = DataAssetBatchEditService.PreviewSetValue(new[] { asset }, "missingField", "value");

                Assert.AreEqual(1, preview.BlockedCount);
                Assert.AreEqual(DataAssetFieldChangeState.MissingField, preview.Changes.Single().State);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ReferenceGraphReportsOutgoingAndIncomingEdges()
        {
            var source = ScriptableObject.CreateInstance<ImportableConfigSO>();
            var target = ScriptableObject.CreateInstance<ImportableConfigSO>();
            try
            {
                source.name = "SourceConfig";
                target.name = "TargetConfig";
                source.SetTarget(target);

                var graph = DataAssetReferenceGraphService.Build(new[] { source, target });

                var outgoing = graph.GetOutgoing("SourceConfig");
                var incoming = graph.GetIncoming("TargetConfig");
                Assert.AreEqual(1, outgoing.Count);
                Assert.AreEqual("target", outgoing.Single().FieldPath);
                Assert.AreEqual("TargetConfig", outgoing.Single().TargetName);
                Assert.AreEqual(1, incoming.Count);
                Assert.AreEqual("SourceConfig", incoming.Single().SourceName);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
            }
        }

        [CreateAssetMenu(fileName = "MenuBackedConfigAsset", menuName = "ZeroEngine/Tests/Menu Backed Config")]
        private sealed class MenuBackedConfigAsset : ScriptableObject
        {
        }

        private sealed class SuffixConfigSO : ScriptableObject
        {
            [SerializeField]
            private string id;

            public void SetId(string value)
            {
                id = value;
            }
        }

        private sealed class ImportableConfigSO : ScriptableObject
        {
            [SerializeField]
            private string id;

            [SerializeField]
            private int cost;

            [SerializeField]
            private bool enabled;

            [SerializeField]
            private ScriptableObject target;

            public string Id => id;
            public int Cost => cost;
            public bool Enabled => enabled;

            public void SetValues(string newId, int newCost, bool newEnabled)
            {
                id = newId;
                cost = newCost;
                enabled = newEnabled;
            }

            public void SetTarget(ScriptableObject newTarget)
            {
                target = newTarget;
            }
        }

        private sealed class DesignerConfigSO : ScriptableObject
        {
            public string DisplayName;
            public int Cost;
            public float DropChance;
            public int MinLevel;
            public int MaxLevel;
            public List<ScriptableObject> Rewards = new();
        }

        private sealed class PackageBridgeConfigSO : ScriptableObject
        {
        }

        private enum PackageBridgeValidationSeverity
        {
            Info,
            Warning,
            Error
        }

        private sealed class PackageBridgeValidationIssue
        {
            public PackageBridgeValidationIssue(
                PackageBridgeValidationSeverity severity,
                ScriptableObject asset,
                string assetName,
                string fieldPath,
                string message)
            {
                Severity = severity;
                Asset = asset;
                AssetName = assetName;
                FieldPath = fieldPath;
                Message = message;
            }

            public PackageBridgeValidationSeverity Severity { get; }
            public ScriptableObject Asset { get; }
            public string AssetName { get; }
            public string FieldPath { get; }
            public string Message { get; }
        }

        private static class PackageBridgeConfigValidator
        {
            public static IReadOnlyList<PackageBridgeValidationIssue> Validate(IEnumerable<PackageBridgeConfigSO> configs = null)
            {
                return (configs ?? Enumerable.Empty<PackageBridgeConfigSO>())
                    .Select(config => new PackageBridgeValidationIssue(
                        PackageBridgeValidationSeverity.Error,
                        config,
                        config.name,
                        "BridgeField",
                        "The package bridge rule fired."))
                    .ToArray();
            }
        }

        private sealed class RequiresDisplayNameValidator : IDataToolkitValidationProvider
        {
            public int Order => 0;

            public IEnumerable<DataValidationIssue> Validate(DataToolkitValidationContext context)
            {
                var asset = context.GetAssetsOfType<SuffixConfigSO>().Single();
                yield return new DataValidationIssue(
                    DataValidationSeverity.Error,
                    asset.Type.Name,
                    asset.AssetName,
                    asset.AssetPath,
                    "displayName",
                    "Display name is required by the package rule.");
            }
        }

        private sealed class DiscoveredValidationProvider : IDataToolkitValidationProvider
        {
            public int Order => 1000;

            public IEnumerable<DataValidationIssue> Validate(DataToolkitValidationContext context)
            {
                yield break;
            }
        }
    }
}
