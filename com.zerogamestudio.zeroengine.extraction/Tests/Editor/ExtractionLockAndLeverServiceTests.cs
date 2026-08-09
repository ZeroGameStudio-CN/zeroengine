using NUnit.Framework;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionLockServiceTests
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
        public void Unlock_CompatibleRaidKey_ConsumesDurabilityOnceAndRestoresOpenState()
        {
            var config = CreateConfig();
            var profile = CreateProfileWithKey(config, durability: 2, ExtractionInventoryContainerType.RaidBackpack);

            Assert.IsTrue(ExtractionLockService.TryUnlock(
                profile, config, "lock-a", "key-a", out var firstResult));
            Assert.AreEqual(ExtractionUnlockResult.Unlocked, firstResult);
            Assert.IsTrue(profile.Items.TryGet("key-a", out var key));
            Assert.AreEqual(1, key.CurrentDurability);

            Assert.IsTrue(ExtractionLockService.TryUnlock(
                profile, config, "lock-a", "key-a", out var secondResult));
            Assert.AreEqual(ExtractionUnlockResult.AlreadyUnlocked, secondResult);
            Assert.AreEqual(1, key.CurrentDurability);

            string json = ExtractionProfileSerialization.ToJson(profile);
            var restored = ExtractionProfileSerialization.FromJson(json);
            Assert.Contains("lock-a", restored.ActiveRaid.Content.OpenedLockIds);
            Assert.IsTrue(restored.Items.TryGet("key-a", out var restoredKey));
            Assert.AreEqual(1, restoredKey.CurrentDurability);
        }

        [Test]
        public void Unlock_LastDurability_MovesSameInstanceToDestroyedByUseAndRemovesGridPlacement()
        {
            var config = CreateConfig();
            var profile = CreateProfileWithKey(config, durability: 1, ExtractionInventoryContainerType.RaidBackpack);

            Assert.IsTrue(ExtractionLockService.TryUnlock(
                profile, config, "lock-a", "key-a", out var result));

            Assert.AreEqual(ExtractionUnlockResult.Unlocked, result);
            Assert.AreEqual(
                ExtractionInventoryContainerType.DestroyedByUse,
                profile.Ownership.GetRequiredContainer("key-a"));
            Assert.IsFalse(profile.ActiveRaidInventory.RaidBackpack.TryGetPlacement("key-a", out _));
            Assert.IsTrue(profile.Items.TryGet("key-a", out var key));
            Assert.AreEqual(0, key.CurrentDurability);
        }

        [TestCase(ExtractionInventoryContainerType.EquipmentSlot)]
        [TestCase(ExtractionInventoryContainerType.WorldPickup)]
        [TestCase(ExtractionInventoryContainerType.Stash)]
        public void Unlock_KeyOutsideRaidBackpackOrSecure_IsRejected(
            ExtractionInventoryContainerType location)
        {
            var config = CreateConfig();
            var profile = CreateProfileWithKey(config, durability: 2, location);

            Assert.IsFalse(ExtractionLockService.TryUnlock(
                profile, config, "lock-a", "key-a", out var result));

            Assert.AreEqual(ExtractionUnlockResult.InvalidKeyLocation, result);
            Assert.IsFalse(profile.ActiveRaid.Content.OpenedLockIds.Contains("lock-a"));
            Assert.IsTrue(profile.Items.TryGet("key-a", out var key));
            Assert.AreEqual(2, key.CurrentDurability);
        }

        [Test]
        public void Unlock_ZeroDurability_IsRejectedWithoutOpeningLock()
        {
            var config = CreateConfig();
            var profile = CreateProfileWithKey(config, durability: 0, ExtractionInventoryContainerType.InSecureContainer);

            Assert.IsFalse(ExtractionLockService.TryUnlock(
                profile, config, "lock-a", "key-a", out var result));
            Assert.AreEqual(ExtractionUnlockResult.NoDurability, result);
            Assert.IsFalse(profile.ActiveRaid.Content.OpenedLockIds.Contains("lock-a"));
        }

        private static ExtractionPlayableConfig CreateConfig()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            var key = new ExtractionItemDefinition("key-item", 1, 1, false, 1)
            {
                MaxDurability = 3,
                Tags = new[] { "key" }
            };
            key.CompatibleTargetIds.Add("lock-a");
            config.ItemDefinitions.Add(key);
            config.LockDefinitions.Add(new ExtractionLockDefinition("lock-a", "map-a"));
            return config;
        }

        private static ExtractionProfileSaveData CreateProfileWithKey(
            ExtractionPlayableConfig config,
            int durability,
            ExtractionInventoryContainerType location)
        {
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 20);
            var key = new ExtractionItemInstance("key-a", "key-item", 1)
            {
                CurrentDurability = durability
            };
            Assert.IsTrue(profile.Items.Register(key));
            Assert.IsTrue(profile.Ownership.Register("key-a", location));
            if (location == ExtractionInventoryContainerType.RaidBackpack)
            {
                Assert.IsTrue(profile.ActiveRaidInventory == null);
                profile.ActiveRaidInventory = new ExtractionRaidInventoryState(4, 4, 2, 2);
                Assert.IsTrue(profile.ActiveRaidInventory.RaidBackpack.TryPlace(
                    key, config.ItemDefinitions[config.ItemDefinitions.Count - 1], 0, 0, false));
            }
            else if (location == ExtractionInventoryContainerType.InSecureContainer)
            {
                profile.ActiveRaidInventory = new ExtractionRaidInventoryState(4, 4, 2, 2);
                Assert.IsTrue(profile.ActiveRaidInventory.SecureContainer.TryPlace(
                    key, config.ItemDefinitions[config.ItemDefinitions.Count - 1], 0, 0, false));
            }

            return profile;
        }
    }

    public class ExtractionLeverServiceTests
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
        public void Activate_ApprovedEffectsCommitTogetherAndAreIdempotent()
        {
            var config = CreateConfig();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 31);
            var manifest = profile.ActiveRaid.Content.LootManifest;
            manifest.Containers[0].BonusGroupId = "bonus-a";
            manifest.Containers[0].Active = false;

            Assert.IsTrue(ExtractionLeverService.TryActivate(
                profile, config, "lever-a", out var firstResult));

            Assert.AreEqual(ExtractionLeverActivationResult.Activated, firstResult);
            Assert.AreEqual(120, profile.ActiveRaid.Content.DeadlineExtensionSeconds);
            Assert.AreEqual(1, profile.ActiveRaid.Content.ThreatLevelDelta);
            Assert.AreEqual(
                profile.ActiveRaid.ThreatLevel + 1,
                ExtractionRaidPressureService.GetEffectiveThreatLevel(profile.ActiveRaid));
            Assert.IsTrue(manifest.Containers[0].Active);
            Assert.Contains("bonus-a", profile.ActiveRaid.Content.UnlockedBonusContainerGroupIds);
            Assert.IsTrue(profile.ActiveRaid.HasRaidFlag("power.on"));
            Assert.AreEqual(
                120,
                ExtractionRaidPressureService.GetEffectiveOpenAtElapsedSeconds(
                    profile.ActiveRaid,
                    config.ExtractionPoints[0]));
            Assert.AreEqual(720, ExtractionRaidPressureService.GetEffectiveDurationSeconds(profile.ActiveRaid));

            Assert.IsTrue(ExtractionLeverService.TryActivate(
                profile, config, "lever-a", out var secondResult));
            Assert.AreEqual(ExtractionLeverActivationResult.AlreadyActivated, secondResult);
            Assert.AreEqual(120, profile.ActiveRaid.Content.DeadlineExtensionSeconds);
            Assert.AreEqual(1, profile.ActiveRaid.Content.ThreatLevelDelta);
        }

        [Test]
        public void Activate_InvalidLaterEffect_LeavesEarlierEffectsUnchanged()
        {
            var config = CreateConfig();
            config.LeverDefinitions[0].Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.AdvanceExtractionPointSeconds,
                "missing-point",
                30));
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 32);
            profile.ActiveRaid.Content.LootManifest.Containers[0].BonusGroupId = "bonus-a";
            profile.ActiveRaid.Content.LootManifest.Containers[0].Active = false;

            Assert.IsFalse(ExtractionLeverService.TryActivate(
                profile, config, "lever-a", out var result));

            Assert.AreEqual(ExtractionLeverActivationResult.MissingTarget, result);
            Assert.AreEqual(0, profile.ActiveRaid.Content.DeadlineExtensionSeconds);
            Assert.AreEqual(0, profile.ActiveRaid.Content.ThreatLevelDelta);
            Assert.IsFalse(profile.ActiveRaid.HasRaidFlag("power.on"));
            Assert.IsFalse(profile.ActiveRaid.Content.ActivatedLeverIds.Contains("lever-a"));
        }

        [Test]
        public void ExtractionPoint_SingleUseAndEffectiveDeadline_ReadCommittedRaidState()
        {
            var config = CreateConfig();
            var profile = ExtractionLootRuntimeFixture.CreateActiveRaidProfile(config, 33);
            var raid = profile.ActiveRaid;
            var point = config.ExtractionPoints[0];
            raid.Content.ExtractionPointStates.Add(new ExtractionPointRuntimeState(point.PointId, 10));

            Assert.IsFalse(ExtractionRaidPressureService.CanStartExtraction(raid, point, 1005, false));
            Assert.IsTrue(ExtractionRaidPressureService.CanStartExtraction(raid, point, 1010, false));
            raid.Content.UsedExtractionPointIds.Add(point.PointId);
            Assert.IsFalse(ExtractionRaidPressureService.CanStartExtraction(raid, point, 1010, false));
        }

        private static ExtractionPlayableConfig CreateConfig()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfigWithoutGuarantees();
            config.ExtractionPoints.Add(new ExtractionPointDefinition(
                "extract-timed", "map-a", 3, false, 300, true, false));
            var lever = new ExtractionLeverDefinition("lever-a", "map-a");
            lever.Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.ExtendRaidDeadlineSeconds, null, 120));
            lever.Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.AddThreatLevel, null, 1));
            lever.Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.UnlockBonusContainerGroup, "bonus-a", 1));
            lever.Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.AdvanceExtractionPointSeconds, "extract-timed", 180, 60));
            lever.Effects.Add(new ExtractionLeverEffectDefinition(
                ExtractionLeverEffectType.SetRaidFlag, "power.on", 1));
            config.LeverDefinitions.Add(lever);
            return config;
        }
    }
}
