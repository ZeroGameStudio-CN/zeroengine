using System;
using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    // Permanent regression: a raid is the lifetime boundary for dropped items, including old saves.
    public class ExtractionRaidWorldLifetimeTests
    {
        [SetUp] public void SetUp() => ExtractionFeatureSwitch.SetEnabledForTests(true);
        [TearDown] public void TearDown() => ExtractionFeatureSwitch.SetEnabledForTests(false);

        [TestCase(true)]
        [TestCase(false)]
        public void Settlement_ExpiresWorldItemsButPreservesOtherOwnership(bool success)
        {
            var profile = CreateProfile();
            AddWorld(profile, "drop", current: true);
            AddWorld(profile, "old-orphan", current: false);
            profile.Ownership.Register("stash", ExtractionInventoryContainerType.Stash);
            profile.Ownership.Register("corpse", ExtractionInventoryContainerType.Lost);
            profile.Ownership.Register("secure", ExtractionInventoryContainerType.SecureContainer);

            bool settled = success
                ? ExtractionSettlementService.CompleteSuccess(profile, Array.Empty<string>())
                : ExtractionSettlementService.TryCompleteFailure(profile, "failure", "map", Array.Empty<string>(),
                    Array.Empty<string>(), null, out _);

            Assert.IsTrue(settled);
            Assert.IsNull(profile.ActiveRaid);
            AssertExpired(profile, "drop");
            AssertExpired(profile, "old-orphan");
            Assert.AreEqual(ExtractionInventoryContainerType.Stash, profile.Ownership.GetRequiredContainer("stash"));
            Assert.AreEqual(ExtractionInventoryContainerType.Lost, profile.Ownership.GetRequiredContainer("corpse"));
            Assert.AreEqual(ExtractionInventoryContainerType.SecureContainer, profile.Ownership.GetRequiredContainer("secure"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RejectedSettlement_DoesNotExpireWorldItems(bool success)
        {
            var profile = CreateProfile();
            AddWorld(profile, "drop", current: true);
            bool settled = success
                ? ExtractionSettlementService.CompleteSuccess(profile, new[] { "missing" })
                : ExtractionSettlementService.TryCompleteFailure(profile, "failure", "map", new[] { "missing" },
                    Array.Empty<string>(), null, out _);
            Assert.IsFalse(settled);
            Assert.AreEqual(ExtractionInventoryContainerType.WorldPickup, profile.Ownership.GetRequiredContainer("drop"));
            Assert.IsTrue(ExtractionRaidWorldItemService.IsCurrentRaidItem(profile, "drop"));
        }

        [Test]
        public void Migration_PreservesSameRaidDrops_ExpiresLegacyOrphans_Idempotently()
        {
            var profile = CreateProfile();
            AddWorld(profile, "current", current: true);
            AddWorld(profile, "orphan", current: false);
            profile.EnsureInitialized();
            profile.EnsureInitialized();
            Assert.IsTrue(ExtractionRaidWorldItemService.IsCurrentRaidItem(profile, "current"));
            Assert.AreEqual(ExtractionInventoryContainerType.WorldPickup, profile.Ownership.GetRequiredContainer("current"));
            AssertExpired(profile, "orphan");
            Assert.AreEqual(0, ExtractionRaidWorldItemService.ExpireWorldItems(profile, true));
        }

        [Test]
        public void Migration_NoActiveRaid_ExpiresOldDropsWithoutRemovingEvidence()
        {
            var profile = CreateProfile();
            AddWorld(profile, "drop", current: true);
            profile.ActiveRaid = null;
            profile.activeRaidId = null;
            profile.EnsureInitialized();
            AssertExpired(profile, "drop");
        }

        [Test]
        public void Pickup_RepairsOrphanWorldItemAndRejectsCrossRaidPickup()
        {
            var profile = CreateProfile();
            var config = new ExtractionPlayableConfig(4, 4, 2, 2);
            var definition = new ExtractionItemDefinition("item", 1, 1, false, 1);
            config.ItemDefinitions.Add(definition);
            AddWorld(profile, "old", current: false);
            var inventory = new ExtractionRaidInventoryState(4, 4, 2, 2);
            Assert.IsFalse(ExtractionItemLifecycleService.TryPickupWorldItem(profile, inventory, config,
                "old", ExtractionInventoryContainerType.RaidBackpack, out var result));
            Assert.AreEqual(ExtractionItemLifecycleResult.LocationConflict, result);
            AssertExpired(profile, "old");
        }

        private static ExtractionProfileSaveData CreateProfile()
        {
            var profile = ExtractionProfileSaveData.CreateEmpty();
            profile.ActiveRaid = new ExtractionRaidSession(new ExtractionMapDefinition("map", "room", 900, 1, false),
                new ExtractionRaidStartRequest("raid-current", 1, 100));
            profile.activeRaidId = profile.ActiveRaid.RaidId;
            return profile;
        }

        private static void AddWorld(ExtractionProfileSaveData profile, string id, bool current)
        {
            profile.Items.Register(new ExtractionItemInstance(id, "item", 1));
            profile.Ownership.Register(id, ExtractionInventoryContainerType.WorldPickup, "world-pickup", "position-" + id);
            if (current) profile.ActiveRaid.Content.WorldPickupItemInstanceIds.Add(id);
        }

        private static void AssertExpired(ExtractionProfileSaveData profile, string id)
        {
            var entry = profile.Ownership.Entries.Find(candidate => candidate.ItemInstanceId == id);
            Assert.AreEqual(ExtractionInventoryContainerType.Destroyed, entry.Container);
            Assert.AreEqual(ExtractionRaidWorldItemService.ExpiredLocationSubtype, entry.LocationSubtype);
            Assert.AreEqual("position-" + id, entry.LocationId);
            Assert.IsTrue(profile.Items.TryGet(id, out _), "Keep the original item as repair evidence.");
        }
    }
}
