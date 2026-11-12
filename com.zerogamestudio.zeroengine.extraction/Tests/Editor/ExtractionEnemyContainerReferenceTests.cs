using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    // Permanent regression: enemy references resolve the shared container pool without changing legacy data.
    public sealed class ExtractionEnemyContainerReferenceTests
    {
        [TestCase(0, "first")]
        [TestCase(1, "second")]
        [TestCase(2, "second")]
        public void ContainerReference_UsesEveryTableAndEntryWeight(int roll, string expected)
        {
            var config = CreateConfig();
            var encounter = Encounter();
            Assert.IsTrue(encounter.IsValid);
            Assert.IsTrue(ExtractionHostileExplorerLootService.TryRollExplorerLoot(encounter, config, config,
                "drop", roll, out var pickup));
            Assert.AreEqual(expected, pickup.Item.DefinitionId);
            Assert.AreEqual(expected == "first" ? 1 : 2, pickup.Item.Quantity);
        }

        [Test]
        public void InvalidContainer_DoesNotFallBackToLegacyTableOrPartiallyRoll()
        {
            var config = CreateConfig();
            var encounter = Encounter();
            encounter.LootTableId = "table-a";
            encounter.ContainerTypeId = "table-a";
            Assert.IsFalse(ExtractionHostileExplorerLootService.TryRollExplorerLoot(encounter, config, config, "drop", 0, out _));
            encounter.ContainerTypeId = "container";
            config.ContainerDefinitions[0].LootTableIds.Add("missing-table");
            Assert.IsFalse(ExtractionHostileExplorerLootService.TryRollExplorerLoot(encounter, config, config, "drop", 0, out _));
        }

        [Test]
        public void SerializedReferences_PreserveLegacyAndContainerPrecedence()
        {
            var config = CreateConfig();
            var old = JsonUtility.FromJson<ExtractionHostileExplorerDefinition>(
                "{\"EncounterId\":\"enemy\",\"MapId\":\"map\",\"ActorKey\":\"actor\",\"LootTableId\":\"table-a\",\"Weight\":1}");
            Assert.IsTrue(ExtractionHostileExplorerLootService.TryRollExplorerLoot(old, config, config, "drop", 1, out var legacy));
            Assert.AreEqual("first", legacy.Item.DefinitionId);
            old.ContainerTypeId = "container";
            var copy = JsonUtility.FromJson<ExtractionHostileExplorerDefinition>(JsonUtility.ToJson(old));
            Assert.AreEqual("container", copy.ContainerTypeId);
            Assert.AreEqual("table-a", copy.LootTableId);
            Assert.IsTrue(ExtractionHostileExplorerLootService.TryRollExplorerLoot(copy, config, config, "drop", 1, out var current));
            Assert.AreEqual("second", current.Item.DefinitionId);
        }

        private static ExtractionHostileExplorerDefinition Encounter() =>
            new ExtractionHostileExplorerDefinition("enemy", "map", "actor", null, 0, 1) { ContainerTypeId = "container" };

        private static ExtractionPlayableConfig CreateConfig()
        {
            var config = new ExtractionPlayableConfig(2, 2, 1, 1);
            config.ItemDefinitions.Add(new ExtractionItemDefinition("first", 1, 1, false, 10));
            config.ItemDefinitions.Add(new ExtractionItemDefinition("second", 1, 1, false, 10));
            var first = new ExtractionLootTableDefinition("table-a");
            first.Entries.Add(new ExtractionLootTableEntry("first", 1, 1, true));
            var second = new ExtractionLootTableDefinition("table-b");
            second.Entries.Add(new ExtractionLootTableEntry("second", 2, 2, false));
            config.LootTables.Add(first);
            config.LootTables.Add(second);
            var container = new ExtractionContainerDefinition("container", 3, 1, 3, 1);
            container.LootTableIds.Add("table-a");
            container.LootTableIds.Add("table-b");
            config.ContainerDefinitions.Add(container);
            return config;
        }
    }
}
