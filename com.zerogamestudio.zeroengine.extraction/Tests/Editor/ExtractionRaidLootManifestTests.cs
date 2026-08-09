using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionRaidLootManifestTests
    {
        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void Generate_SameSeedAndConfig_ProducesIdenticalManifest()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            var map = config.Maps[0];

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, map, 12345, false, out var first, out var firstFailure), firstFailure.ToString());
            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, map, 12345, false, out var second, out var secondFailure), secondFailure.ToString());

            Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second));
            Assert.AreEqual(first.ManifestId, second.ManifestId);
        }

        [Test]
        public void Generate_DifferentSeed_ProducesDifferentManifestIdentity()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, config.Maps[0], 100, false, out var first, out _));
            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, config.Maps[0], 101, false, out var second, out _));

            Assert.AreNotEqual(first.ManifestId, second.ManifestId);
        }

        [Test]
        public void Generate_MinimumRarityQuota_IsPrecommittedWithoutOrdinarySlots()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, config.Maps[0], 77, false, out var manifest, out var failure), failure.ToString());

            Assert.AreEqual(2, ExtractionLootRuntimeFixture.CountGuaranteed(manifest, ExtractionItemRarity.Common));
            Assert.AreEqual(1, ExtractionLootRuntimeFixture.CountGuaranteed(manifest, ExtractionItemRarity.Uncommon));
            Assert.AreEqual(1, ExtractionLootRuntimeFixture.CountGuaranteed(manifest, ExtractionItemRarity.Rare));
            foreach (var container in manifest.Containers)
            {
                Assert.IsFalse(container.Opened);
                Assert.LessOrEqual(container.Entries.Count, container.TargetContentCount);
                Assert.LessOrEqual(container.TargetContentCount, container.MaximumContentCount);
            }
        }

        [Test]
        public void Generate_RareLootDisabled_FiltersRareQuotaAndRareEntries()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, config.Maps[0], 77, true, out var manifest, out var failure), failure.ToString());

            Assert.AreEqual(0, ExtractionLootRuntimeFixture.CountGuaranteed(manifest, ExtractionItemRarity.Rare));
            foreach (var container in manifest.Containers)
            foreach (var entry in container.Entries)
                Assert.Less(entry.Rarity, ExtractionItemRarity.Rare);
        }

        [Test]
        public void Generate_InsufficientCapacity_FailsDeterministically()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity =
                new ExtractionRarityIntValues(0, 0, 9, 0, 0, 0);

            Assert.IsFalse(ExtractionRaidLootManifestGenerator.TryGenerate(
                config, config.Maps[0], 77, false, out var manifest, out var failure));
            Assert.IsNull(manifest);
            Assert.AreEqual(ExtractionRaidLootManifestFailure.InsufficientGuaranteedCapacity, failure);
        }

        [Test]
        public void SessionFactory_ManifestFailure_DoesNotMoveOwnershipOrCreateRaid()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity =
                new ExtractionRarityIntValues(0, 0, 9, 0, 0, 0);
            var profile = ExtractionProfileSaveData.CreateEmpty();
            profile.Items.Register(new ExtractionItemInstance("loadout-item", "common-item", 1));
            profile.Ownership.Register("loadout-item", ExtractionInventoryContainerType.Loadout);
            var request = new ExtractionRaidStartRequest("raid-a", 77, 1000);
            request.LoadoutItemInstanceIds.Add("loadout-item");

            Assert.IsFalse(ExtractionRaidSessionFactory.TryCreate(
                profile,
                config,
                config.Maps[0],
                request,
                false,
                out var session,
                out var failure));

            Assert.IsNull(session);
            Assert.AreEqual(ExtractionRaidLootManifestFailure.InsufficientGuaranteedCapacity, failure);
            Assert.IsNull(profile.ActiveRaid);
            Assert.IsNull(profile.activeRaidId);
            Assert.AreEqual(
                ExtractionInventoryContainerType.Loadout,
                profile.Ownership.GetRequiredContainer("loadout-item"));
        }
    }

    public class ExtractionContainerLootServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void Open_FirstOpenCommitsResultsOrderPityAndReceipt_ReopenDoesNotReroll()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 42);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;

            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile, config, containerId, out var opened, out var openResult));
            Assert.AreEqual(ExtractionContainerOpenResult.Opened, openResult);
            string firstJson = JsonUtility.ToJson(opened);
            int firstPity = profile.ActiveRaid.Content.LootManifest.PityState.ConsecutiveMisses;

            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile, config, containerId, out var reopened, out var reopenResult));
            Assert.AreEqual(ExtractionContainerOpenResult.AlreadyOpened, reopenResult);
            Assert.AreEqual(firstJson, JsonUtility.ToJson(reopened));
            Assert.AreEqual(firstPity, profile.ActiveRaid.Content.LootManifest.PityState.ConsecutiveMisses);
            Assert.IsNotEmpty(opened.OpenReceiptId);
            Assert.AreEqual(opened.Entries.Count, ExtractionLootRuntimeFixture.CountDistinctRevealOrders(opened));
            foreach (var entry in opened.Entries)
                Assert.AreEqual(ExtractionContainerLootEntryState.CommittedHidden, entry.State);
        }

        [Test]
        public void Open_RoundTrip_PreservesCommittedResultByteForByte()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 19);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, containerId, out _, out _));
            string before = ExtractionProfileSerialization.ToJson(profile);

            var reloaded = ExtractionProfileSerialization.FromJson(before);

            Assert.AreEqual(before, ExtractionProfileSerialization.ToJson(reloaded));
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                reloaded, config, containerId, out _, out var result));
            Assert.AreEqual(ExtractionContainerOpenResult.AlreadyOpened, result);
            Assert.AreEqual(before, ExtractionProfileSerialization.ToJson(reloaded));
        }

        [Test]
        public void Open_RareLootDisabled_NeverUsesRareCandidateAndPityStaysReset()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 91, true);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;

            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile, config, containerId, out var container, out _));

            foreach (var entry in container.Entries)
                Assert.Less(entry.Rarity, ExtractionItemRarity.Rare);
            Assert.AreEqual(0, profile.ActiveRaid.Content.LootManifest.PityState.ConsecutiveMisses);
        }
    }

    public class ExtractionContainerSearchServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(true);
        }

        [TearDown]
        public void TearDown()
        {
            ExtractionFeatureSwitch.SetEnabledForTests(false);
        }

        [Test]
        public void Search_PauseFreezeResumeAndReveal_UsesCommittedProgress()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 12);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile, config, containerId, out var container, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryStart(
                profile.ActiveRaid, containerId, out _));

            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 0f, 1f, out var frozenReveal, out var frozenResult));
            Assert.IsNull(frozenReveal);
            Assert.AreEqual(ExtractionContainerSearchResult.Progressed, frozenResult);
            Assert.AreEqual(0f, container.SearchState.CurrentEntryElapsedSeconds);

            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 0.2f, 1f, out _, out _));
            float savedProgress = container.SearchState.CurrentEntryElapsedSeconds;
            Assert.Greater(savedProgress, 0f);
            Assert.IsTrue(ExtractionContainerSearchService.TryPause(profile.ActiveRaid, containerId, out _));
            Assert.IsFalse(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 1f, 1f, out _, out var pausedResult));
            Assert.AreEqual(ExtractionContainerSearchResult.NotActive, pausedResult);
            Assert.AreEqual(savedProgress, container.SearchState.CurrentEntryElapsedSeconds);

            Assert.IsTrue(ExtractionContainerSearchService.TryStart(profile.ActiveRaid, containerId, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 10f, 1f, out var revealed, out var revealResult));
            Assert.IsNotNull(revealed);
            Assert.AreEqual(ExtractionContainerLootEntryState.Revealed, revealed.State);
            Assert.IsTrue(
                revealResult == ExtractionContainerSearchResult.Revealed
                || revealResult == ExtractionContainerSearchResult.Completed);
            Assert.IsNotEmpty(revealed.RevealReceiptId);
        }

        [Test]
        public void Search_HiddenEntryCannotTransfer_RevealedEntryCanTransferOnce()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 13);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile, config, containerId, out var container, out _));
            var first = ExtractionLootRuntimeFixture.GetEntryAtRevealOrder(container, 0);

            Assert.IsFalse(ExtractionContainerSearchService.CanTransfer(
                profile.ActiveRaid, containerId, first.EntryId));
            Assert.IsFalse(ExtractionContainerSearchService.TryMarkTransferred(
                profile.ActiveRaid, containerId, first.EntryId, "transfer-a"));

            Assert.IsTrue(ExtractionContainerSearchService.TryStart(profile.ActiveRaid, containerId, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 10f, 1f, out var revealed, out _));
            Assert.IsTrue(ExtractionContainerSearchService.CanTransfer(
                profile.ActiveRaid, containerId, revealed.EntryId));
            Assert.IsTrue(ExtractionContainerSearchService.TryMarkTransferred(
                profile.ActiveRaid, containerId, revealed.EntryId, "transfer-a"));
            Assert.IsFalse(ExtractionContainerSearchService.TryMarkTransferred(
                profile.ActiveRaid, containerId, revealed.EntryId, "transfer-a"));
        }

        [Test]
        public void Search_RoundTrip_PreservesCurrentEntryProgress()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 14);
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(profile, config, containerId, out _, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryStart(profile.ActiveRaid, containerId, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid, config, containerId, 0.2f, 1f, out _, out _));
            string json = ExtractionProfileSerialization.ToJson(profile);

            var restored = ExtractionProfileSerialization.FromJson(json);
            var restoredManifest = restored.ActiveRaid.Content.LootManifest;
            Assert.IsTrue(restoredManifest.TryGetContainer(containerId, out var restoredContainer));

            Assert.AreEqual(0.2f, restoredContainer.SearchState.CurrentEntryElapsedSeconds, 0.0001f);
            Assert.AreEqual(containerId, restoredManifest.ActiveSearchContainerId);
            Assert.IsFalse(restoredContainer.SearchState.Paused);
        }

        [Test]
        public void Transfer_RevealedEntryRegistersSameInstanceOnceInRaidBackpack()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 77);
            var inventory = new ExtractionRaidInventoryState(6, 4, 2, 2);
            profile.ActiveRaidInventory = inventory;
            string containerId = profile.ActiveRaid.Content.LootManifest.Containers[0].ContainerId;
            Assert.IsTrue(ExtractionContainerLootService.TryOpen(
                profile,
                config,
                containerId,
                out _,
                out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryStart(profile.ActiveRaid, containerId, out _));
            Assert.IsTrue(ExtractionContainerSearchService.TryAdvance(
                profile.ActiveRaid,
                config,
                containerId,
                10f,
                1f,
                out var revealed,
                out _));

            Assert.IsTrue(ExtractionContainerTransferService.TryTransfer(
                profile,
                inventory,
                config,
                containerId,
                revealed.EntryId,
                ExtractionInventoryContainerType.RaidBackpack,
                "transfer-77",
                out string itemInstanceId,
                out var result));

            Assert.AreEqual(ExtractionContainerTransferResult.Succeeded, result);
            Assert.AreEqual(revealed.ItemInstanceId, itemInstanceId);
            Assert.AreEqual(ExtractionContainerLootEntryState.Transferred, revealed.State);
            Assert.AreEqual("transfer-77", revealed.TransferReceiptId);
            Assert.IsTrue(profile.Items.TryGet(itemInstanceId, out var item));
            Assert.IsTrue(item.HasFlag(ExtractionItemInstanceFlags.PolicyInitialized));
            Assert.AreEqual(
                ExtractionInventoryContainerType.RaidBackpack,
                profile.Ownership.GetRequiredContainer(itemInstanceId));
            Assert.IsTrue(inventory.RaidBackpack.TryGetPlacement(itemInstanceId, out _));

            Assert.IsTrue(ExtractionContainerTransferService.TryTransfer(
                profile,
                inventory,
                config,
                containerId,
                revealed.EntryId,
                ExtractionInventoryContainerType.RaidBackpack,
                "transfer-77",
                out var replayedItemId,
                out var replayResult));
            Assert.AreEqual(itemInstanceId, replayedItemId);
            Assert.AreEqual(ExtractionContainerTransferResult.AlreadyTransferred, replayResult);
        }

        [Test]
        public void FeatureOff_OpenAndSearchDoNotMutateCommittedContainerState()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 891);
            var container = profile.ActiveRaid.Content.LootManifest.Containers[0];
            int initialSequence = profile.ActiveRaid.Content.LootManifest.NextOpenSequence;

            ExtractionFeatureSwitch.SetEnabledForTests(false);

            Assert.IsFalse(ExtractionContainerLootService.TryOpen(
                profile,
                config,
                container.ContainerId,
                out _,
                out _));
            Assert.IsFalse(ExtractionContainerSearchService.TryStart(
                profile.ActiveRaid,
                container.ContainerId,
                out _));
            Assert.IsFalse(container.Opened);
            Assert.AreEqual(initialSequence, profile.ActiveRaid.Content.LootManifest.NextOpenSequence);
            Assert.IsNull(profile.ActiveRaid.Content.LootManifest.ActiveSearchContainerId);
        }
    }

    internal static class ExtractionLootRuntimeFixture
    {
        public static ExtractionPlayableConfig CreateConfigWithoutGuarantees()
        {
            var config = CreateConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity = new ExtractionRarityIntValues();
            return config;
        }

        public static ExtractionPlayableConfig CreateConfig()
        {
            var config = new ExtractionPlayableConfig(6, 4, 2, 2);
            var map = new ExtractionMapDefinition("map-a", "RoomA", 600, 1, true)
            {
                ContentTierId = "tier-a",
                LootProfileId = "profile-a"
            };
            config.Maps.Add(map);
            config.ContentTiers.Add(new ExtractionContentTierDefinition(
                "tier-a",
                new ExtractionRarityFloatValues(1f, 1f, 1f, 1f, 1f, 1f)));
            config.LootProfiles.Add(new ExtractionLootProfileDefinition("profile-a", "tier-a")
            {
                MinimumGeneratedDropsByRarity = new ExtractionRarityIntValues(2, 1, 1, 0, 0, 0),
                Pity = new ExtractionLootPityDefinition(ExtractionItemRarity.Rare, 0.5f, 3f),
                RevealTimes = new ExtractionRevealTimeDefinition(
                    new ExtractionRarityFloatValues(0.5f, 0.8f, 1.2f, 1.8f, 2.6f, 3.6f)),
                RegionIds = { "region-a" }
            });
            config.LootRegions.Add(new ExtractionLootRegionDefinition(
                "region-a",
                new ExtractionRarityFloatValues(1f, 1f, 1f, 1f, 1f, 1f))
            {
                AllowedContainerTypeIds = { "container-a" },
                ContainerSpawnIds = { "spawn-a", "spawn-b" }
            });
            var container = new ExtractionContainerDefinition("container-a", 4, 2, 4, 1f);
            container.LootTableIds.Add("table-a");
            config.ContainerDefinitions.Add(container);
            config.ContainerSpawns.Add(CreateSpawn("spawn-a", true, 1f));
            config.ContainerSpawns.Add(CreateSpawn("spawn-b", true, 1f));

            config.ItemDefinitions.Add(CreateItem("common-item", ExtractionItemRarity.Common));
            config.ItemDefinitions.Add(CreateItem("uncommon-item", ExtractionItemRarity.Uncommon));
            config.ItemDefinitions.Add(CreateItem("rare-item", ExtractionItemRarity.Rare));
            var table = new ExtractionLootTableDefinition("table-a");
            table.Entries.Add(new ExtractionLootTableEntry("common-item", 100, 1, true));
            table.Entries.Add(new ExtractionLootTableEntry("uncommon-item", 30, 1, true));
            table.Entries.Add(new ExtractionLootTableEntry("rare-item", 1, 1, true));
            config.LootTables.Add(table);
            return config;
        }

        public static ExtractionProfileSaveData CreateActiveRaidProfile(
            ExtractionPlayableConfig config,
            int seed,
            bool rareLootDisabled = false)
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            var request = new ExtractionRaidStartRequest("raid-" + seed, seed, 1000);
            Assert.IsTrue(ExtractionRaidSessionFactory.TryCreate(
                profile,
                config,
                config.Maps[0],
                request,
                rareLootDisabled,
                out _,
                out var failure), failure.ToString());
            return profile;
        }

        public static int CountGuaranteed(ExtractionRaidLootManifest manifest, ExtractionItemRarity rarity)
        {
            int count = 0;
            foreach (var container in manifest.Containers)
            foreach (var entry in container.Entries)
                if (entry.Guaranteed && entry.Rarity == rarity) count++;
            return count;
        }

        public static int CountDistinctRevealOrders(ExtractionRaidContainerManifest container)
        {
            var orders = new System.Collections.Generic.HashSet<int>();
            foreach (var entry in container.Entries) orders.Add(entry.RevealOrder);
            return orders.Count;
        }

        public static ExtractionContainerLootEntry GetEntryAtRevealOrder(
            ExtractionRaidContainerManifest container,
            int revealOrder)
        {
            foreach (var entry in container.Entries)
                if (entry.RevealOrder == revealOrder) return entry;
            return null;
        }

        private static ExtractionContainerSpawnDefinition CreateSpawn(string id, bool always, float chance)
        {
            var spawn = new ExtractionContainerSpawnDefinition(id, "region-a", always, !always, chance);
            spawn.Candidates.Add(new ExtractionWeightedContainerCandidate("container-a", 1));
            return spawn;
        }

        private static ExtractionItemDefinition CreateItem(string id, ExtractionItemRarity rarity)
        {
            return new ExtractionItemDefinition(id, 1, 1, false, 1) { Rarity = rarity };
        }
    }
}
