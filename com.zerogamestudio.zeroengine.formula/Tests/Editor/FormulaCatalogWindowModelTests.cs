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
