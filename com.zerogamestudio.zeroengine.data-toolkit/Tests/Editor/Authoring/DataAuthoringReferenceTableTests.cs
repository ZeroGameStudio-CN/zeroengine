using System.Linq;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringReferenceTableTests
    {
        [Test]
        public void Build_SortsFiltersAndCapsRows()
        {
            var rows = new[]
            {
                new DataAuthoringReferenceRow("Assets/Prefabs/Hero.prefab", "Prefab", details: "Hero View"),
                new DataAuthoringReferenceRow("Assets/Data/Enemies/Bandit.asset", "数据", stableId: "enemy.bandit"),
                new DataAuthoringReferenceRow("Assets/Scenes/Boot.unity", "场景", details: "Hero Boot"),
                new DataAuthoringReferenceRow(string.Empty, "忽略")
            };

            var result = DataAuthoringReferenceTable.Build(rows, maxRows: 2, searchText: "hero");

            Assert.AreEqual(2, result.TotalCount);
            Assert.AreEqual(2, result.Rows.Count);
            Assert.That(
                result.Rows.Select(row => row.AssetPath),
                Is.EqualTo(new[] { "Assets/Prefabs/Hero.prefab", "Assets/Scenes/Boot.unity" }));
            Assert.False(result.HasOverflow);
        }

        [Test]
        public void Build_ReportsOverflowAfterFiltering()
        {
            var rows = new[]
            {
                new DataAuthoringReferenceRow("Assets/Data/A.asset", "数据"),
                new DataAuthoringReferenceRow("Assets/Data/B.asset", "数据"),
                new DataAuthoringReferenceRow("Assets/Data/C.asset", "数据")
            };

            var result = DataAuthoringReferenceTable.Build(rows, maxRows: 1);

            Assert.AreEqual(3, result.TotalCount);
            Assert.AreEqual(1, result.Rows.Count);
            Assert.True(result.HasOverflow);
        }

        [Test]
        public void Row_NormalizesEmptyKindAndNullValues()
        {
            var row = new DataAuthoringReferenceRow(null, null, null, null, null);

            Assert.AreEqual(string.Empty, row.AssetPath);
            Assert.AreEqual("Reference", row.ReferenceKind);
            Assert.AreEqual(string.Empty, row.AssetType);
            Assert.AreEqual(string.Empty, row.StableId);
            Assert.AreEqual(string.Empty, row.Details);
        }
    }
}
