using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Formula.Editor;
using UnityObject = UnityEngine.Object;

namespace ZeroEngine.Formula.Tests.Editor
{
    [TestFixture]
    public sealed class FormulaCatalogWindowModelTests
    {
        [Test]
        public void BuildRows_CombinesCatalogReferencesAndScanIssues()
        {
            var report = new FormulaAssetScanReport { AssetCount = 1 };
            report.AddIssue(
                FormulaAssetScanSeverity.Warning,
                "Assets/Data/CoinFormula.asset",
                "公式名称像临时命名。");

            var rows = FormulaCatalogWindowModel.BuildRows(
                new[]
                {
                    new FormulaCatalogAssetRecord(
                        "Assets/Data/CoinFormula.asset",
                        "guid-1",
                        "CoinFormula",
                        null),
                },
                new FormulaCatalogLookup(new[]
                {
                    new FormulaCatalogEntry(
                        null,
                        "guid-1",
                        "金币收益",
                        "计算金币掉落",
                        "系统",
                        "coin",
                        new[] { "reward" },
                        FormulaCatalogStatus.Active,
                        FormulaResultRange.None,
                        string.Empty),
                }),
                new[]
                {
                    new FormulaAssetReference("Assets/Data/Ability.asset", "guid-1", "guid"),
                    new FormulaAssetReference("Assets/AddressableAssetsData/Groups/Math.asset", "guid-1", "addressables"),
                },
                report);

            var row = rows.Single();
            Assert.That(row.AssetPath, Is.EqualTo("Assets/Data/CoinFormula.asset"));
            Assert.That(row.ReferenceCount, Is.EqualTo(2));
            Assert.That(row.WarningCount, Is.EqualTo(1));
            Assert.That(row.ErrorCount, Is.EqualTo(0));
            Assert.That(row.HasCatalogEntry, Is.True);
            Assert.That(row.Status, Is.EqualTo(FormulaCatalogStatus.Active));
            Assert.That(row.Title, Is.EqualTo("金币收益"));
            Assert.That(row.Purpose, Is.EqualTo("计算金币掉落"));
        }

        [Test]
        public void FilterRows_SearchTextMatchesCatalogMetadataAndIssues()
        {
            var report = new FormulaAssetScanReport { AssetCount = 2 };
            report.AddIssue(
                FormulaAssetScanSeverity.Warning,
                "Assets/Data/CoinFormula.asset",
                "需要策划复核期望范围。");

            var rows = FormulaCatalogWindowModel.BuildRows(
                new[]
                {
                    new FormulaCatalogAssetRecord(
                        "Assets/Data/CoinFormula.asset",
                        "guid-1",
                        "CoinFormula",
                        null),
                    new FormulaCatalogAssetRecord(
                        "Assets/Data/DamageFormula.asset",
                        "guid-2",
                        "DamageFormula",
                        null),
                },
                new FormulaCatalogLookup(new[]
                {
                    new FormulaCatalogEntry(
                        null,
                        "guid-1",
                        "金币收益",
                        "关卡掉落奖励",
                        "策划",
                        "coin",
                        new[] { "reward", "economy" },
                        FormulaCatalogStatus.Active,
                        FormulaResultRange.None,
                        "主线奖励公式"),
                    new FormulaCatalogEntry(
                        null,
                        "guid-2",
                        "伤害",
                        "战斗伤害",
                        "战斗",
                        "damage",
                        new[] { "combat" },
                        FormulaCatalogStatus.Active,
                        FormulaResultRange.None,
                        string.Empty),
                }),
                Array.Empty<FormulaAssetReference>(),
                report);

            var metadataMatches = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.All, "economy 策划");
            var issueMatches = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.All, "复核");
            var assetMatches = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.All, "coinformula");
            var misses = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.All, "boss");

            Assert.That(metadataMatches.Select(row => row.FormulaGuid).ToArray(), Is.EqualTo(new[] { "guid-1" }));
            Assert.That(issueMatches.Select(row => row.FormulaGuid).ToArray(), Is.EqualTo(new[] { "guid-1" }));
            Assert.That(assetMatches.Select(row => row.FormulaGuid).ToArray(), Is.EqualTo(new[] { "guid-1" }));
            Assert.That(misses, Is.Empty);
        }

        [Test]
        public void FilterRows_CombinesIssueFilterAndSearchText()
        {
            var report = new FormulaAssetScanReport { AssetCount = 2 };
            report.AddIssue(
                FormulaAssetScanSeverity.Error,
                "Assets/Data/BrokenFormula.asset",
                "除零错误。");
            report.AddIssue(
                FormulaAssetScanSeverity.Warning,
                "Assets/Data/WarningFormula.asset",
                "未引用。");

            var rows = FormulaCatalogWindowModel.BuildRows(
                new[]
                {
                    new FormulaCatalogAssetRecord(
                        "Assets/Data/BrokenFormula.asset",
                        "guid-1",
                        "BrokenFormula",
                        null),
                    new FormulaCatalogAssetRecord(
                        "Assets/Data/WarningFormula.asset",
                        "guid-2",
                        "WarningFormula",
                        null),
                },
                null,
                Array.Empty<FormulaAssetReference>(),
                report);

            var matches = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.Errors, "broken");
            var filteredOutBySearch = FormulaCatalogWindowModel.FilterRows(rows, FormulaCatalogWindowFilter.Errors, "warning");

            Assert.That(matches.Select(row => row.FormulaGuid).ToArray(), Is.EqualTo(new[] { "guid-1" }));
            Assert.That(filteredOutBySearch, Is.Empty);
        }

        [Test]
        public void AddMissingEntries_AddsOnlyMissingGuids()
        {
            var catalog = ScriptableObject.CreateInstance<FormulaCatalog>();

            try
            {
                var existing = FormulaCatalogWindowModel.CreateDraftEntry(
                    null,
                    "guid-1",
                    "Assets/Data/ExistingFormula.asset");
                var missing = FormulaCatalogWindowModel.CreateDraftEntry(
                    null,
                    "guid-2",
                    "Assets/Data/MissingFormula.asset");

                catalog.AddMissingEntries(new[] { existing });
                var added = catalog.AddMissingEntries(new[] { existing, missing });

                Assert.That(added, Is.EqualTo(1));
                Assert.That(catalog.Entries.Count, Is.EqualTo(2));
                Assert.That(catalog.Entries.Select(entry => entry.FormulaGuid).ToArray(), Is.EqualTo(new[] { "guid-1", "guid-2" }));
            }
            finally
            {
                UnityObject.DestroyImmediate(catalog);
            }
        }
    }
}
