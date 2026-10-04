using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    // Permanent regression: generation reserves real geometry without changing committed legacy loot.
    public sealed class ExtractionContainerGenerationTests
    {
        [SetUp] public void Enable() => ExtractionFeatureSwitch.SetEnabledForTests(true);
        [TearDown] public void Disable() => ExtractionFeatureSwitch.SetEnabledForTests(false);

        [TestCase(2, 3, 2, 2, false, 1)]
        [TestCase(3, 3, 2, 2, false, 1)]
        [TestCase(3, 2, 2, 3, false, 0)]
        [TestCase(3, 2, 2, 3, true, 1)]
        [TestCase(2, 3, 1, 2, false, 2)]
        public void Open_RespectsBoundsOverlapAndRotation(int columns, int rows,
            int width, int height, bool canRotate, int expected)
        {
            var config = Config(columns, rows, width, height, canRotate);
            foreach (bool twoStage in new[] { false, true })
            {
                var profile = Profile(config, twoStage);
                Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, "spawn-a", out var container, out _));
                Assert.AreEqual(expected, container.Entries.Count);
                AssertFits(container, config);
                if (expected > 0 && width == 2 && height == 3 && canRotate)
                    Assert.IsTrue(container.Entries[0].Layout.Rotated);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Open_FiltersOversizedCandidatesBeforeWeightedSelection(bool twoStage)
        {
            var config = Config(2, 3, 5, 5, true);
            config.ItemDefinitions.Add(new ExtractionItemDefinition("small", 1, 1, false, 1));
            config.LootTables[0].Entries.Add(new ExtractionLootTableEntry("small", 1, 1, false));
            config.LootTables[0].Entries[0].Weight = 1000000;
            string before = JsonUtility.ToJson(config);
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(Profile(config, twoStage), config,
                "spawn-a", out var container, out _));
            Assert.AreEqual(2, container.Entries.Count);
            foreach (var entry in container.Entries) Assert.AreEqual("small", entry.DefinitionId);
            AssertFits(container, config);
            Assert.AreEqual(before, JsonUtility.ToJson(config));
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Guarantees_InsufficientGeometryFailsAtomically(int columns)
        {
            var config = Config(columns, 3, 2, 2, false);
            config.ContainerSpawns.RemoveAt(1);
            config.LootRegions[0].ContainerSpawnIds.Remove("spawn-b");
            config.LootProfiles[0].MinimumGeneratedDropsByRarity = new ExtractionRarityIntValues(2, 0, 0, 0, 0, 0);
            var profile = ExtractionProfileSaveData.CreateEmpty();
            string before = JsonUtility.ToJson(profile);
            Assert.IsFalse(ExtractionRaidSessionFactory.TryCreate(profile, config, config.Maps[0],
                new ExtractionRaidStartRequest("raid", 77, 1000), false, out _, out var failure));
            Assert.AreEqual(ExtractionRaidLootManifestFailure.InsufficientGuaranteedCapacity, failure);
            Assert.AreEqual(before, JsonUtility.ToJson(profile));
        }

        [Test]
        public void Guarantees_UseAnotherContainerWhenFirstHasNoGeometricSpace()
        {
            var config = Config(2, 3, 2, 2, false);
            config.LootProfiles[0].MinimumGeneratedDropsByRarity = new ExtractionRarityIntValues(2, 0, 0, 0, 0, 0);
            var profile = Profile(config, false);
            foreach (var container in profile.ActiveRaid.Content.LootManifest.Containers)
            {
                Assert.AreEqual(1, container.Entries.Count);
                Assert.IsTrue(container.Entries[0].Guaranteed);
                Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, container.ContainerId, out _, out _));
                AssertFits(container, config);
                Assert.AreEqual(1, container.Entries.Count);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SeedReloadAndReopen_PreserveResultAndReceipts(bool twoStage)
        {
            var config = Config(3, 3, 2, 2, false);
            var first = Profile(config, twoStage);
            var second = Profile(config, twoStage);
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(first, config, "spawn-a", out var a, out _));
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(second, config, "spawn-a", out var b, out _));
            AssertFits(a, config);
            AssertFits(b, config);
            Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second));
            first = JsonUtility.FromJson<ExtractionProfileSaveData>(JsonUtility.ToJson(first));
            string saved = JsonUtility.ToJson(first);
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(first, config, "spawn-a", out _, out var result));
            Assert.AreEqual(ExtractionContainerOpenResult.AlreadyOpened, result);
            Assert.AreEqual(saved, JsonUtility.ToJson(first));
        }

        [Test]
        public void LegacyPrecommittedOverflow_IsPreservedAndNoNewLootIsAdded()
        {
            var config = Config(2, 3, 2, 2, false);
            var profile = Profile(config, false);
            var container = profile.ActiveRaid.Content.LootManifest.Containers[0];
            container.TargetContentCount = 3;
            container.Entries.Add(new ExtractionContainerLootEntry("old-a", "item-a", "common-item", 1, 0, true));
            container.Entries.Add(new ExtractionContainerLootEntry("old-b", "item-b", "common-item", 2, 0, true));
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, container.ContainerId, out _, out _));
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(container, config, out _));
            Assert.AreEqual(2, container.Entries.Count);
            Assert.AreEqual("item-a", container.Entries[0].ItemInstanceId);
            Assert.AreEqual("item-b", container.Entries[1].ItemInstanceId);
            Assert.AreEqual(2, container.Entries[1].Quantity);
            Assert.IsTrue(container.Entries[1].LayoutOverflow);
        }

        private static ExtractionPlayableConfig Config(int columns, int rows, int width, int height, bool rotate)
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var container = config.ContainerDefinitions[0];
            container.Columns = columns; container.Rows = rows; container.Capacity = columns * rows;
            container.MinimumContentCount = container.MaximumContentCount = 2;
            var item = config.ItemDefinitions[0]; item.Width = width; item.Height = height; item.CanRotate = rotate;
            config.LootTables[0].Entries.RemoveRange(1, 2);
            return config;
        }

        private static ExtractionProfileSaveData Profile(ExtractionPlayableConfig config, bool twoStage)
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var request = new ExtractionRaidStartRequest("raid", 77, 1000)
            { LootSelectionPolicy = twoStage ? new ExtractionLootSelectionPolicy() : null };
            Assert.IsTrue(ExtractionRaidSessionFactory.TryCreate(profile, config, config.Maps[0], request,
                false, out _, out var failure), failure.ToString());
            return profile;
        }

        private static void AssertFits(ExtractionRaidContainerManifest container, ExtractionPlayableConfig config)
        {
            Assert.IsTrue(ExtractionContainerLayoutService.TryEnsure(container, config, out _));
            var occupied = new ExtractionItemGrid(container.Columns, container.Rows);
            foreach (var entry in container.Entries)
            {
                Assert.IsFalse(entry.LayoutOverflow);
                var p = entry.Layout;
                Assert.IsTrue(occupied.CanPlace(p.X, p.Y, p.Width, p.Height));
                occupied.Placements.Add(p);
            }
        }
    }
}
