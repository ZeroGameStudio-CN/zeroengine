using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace POB.Extraction.Core.Package.Tests.Editor
{
    public class ExtractionRaidMechanicsContractTests
    {
        [Test]
        public void LegacyExtractionPointConstructor_RemainsNormalAndValid()
        {
            var point = new ExtractionPointDefinition("exit", "map-a", 10, true, 0, true, false);

            Assert.AreEqual(ExtractionPointMode.Normal, point.Mode);
            Assert.AreEqual(0, point.TimedWindowSeconds);
            Assert.AreEqual(0, point.RequiredGateCount);
            Assert.AreEqual(0, point.OpenDurationSeconds);
            Assert.AreEqual(0, point.RequiredItemQuantity);
            Assert.IsTrue(point.IsValid);
        }

        [Test]
        public void WeightedSelection_EmptyCandidatesFailsClosed()
        {
            Assert.IsFalse(ExtractionRaidMechanicsService.TrySelectWeightedCandidate(
                new List<ExtractionPointDifficultyCandidate>(),
                17,
                "enemy-point",
                out var selected));
            Assert.IsNull(selected);
        }

        [Test]
        public void WeightedSelection_SameSeedAndPointIsStable()
        {
            var candidates = new List<ExtractionPointDifficultyCandidate>
            {
                new("enemy-point", 1, "enemy-a", 1),
                new("enemy-point", 1, "enemy-b", 3)
            };

            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectWeightedCandidate(
                candidates, 42, "enemy-point", out var first));
            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectWeightedCandidate(
                candidates, 42, "enemy-point", out var second));
            Assert.AreEqual(first.ContentId, second.ContentId);
        }

        [Test]
        public void EnemySpawnPoint_StoresEnemyOrBossKind()
        {
            var point = new ExtractionEnemySpawnPointDefinition(
                "boss-point", "map-a", ExtractionSpawnPointKind.Boss);

            Assert.IsTrue(point.IsValid);
            Assert.AreEqual(ExtractionSpawnPointKind.Boss, point.PointKind);
        }

        [Test]
        public void RandomExtractionPoint_ChoosesFarthestAndStableTie()
        {
            var distances = new Dictionary<string, int>
            {
                ["near"] = 1,
                ["far-a"] = 5,
                ["far-b"] = 5
            };

            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectRandomExtractionPoint(
                distances, 42, out var first));
            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectRandomExtractionPoint(
                distances, 42, out var second));
            Assert.AreEqual(first, second);
            Assert.IsTrue(first == "far-a" || first == "far-b");
        }

        [Test]
        public void Timeline_TriggersTenFiveAndOvertimeOnceWithoutFailure()
        {
            var session = CreateSession(900);
            var rule = new ExtractionRaidDifficultyRuleDefinition(
                1, 600, 300, "reinforce-a", "reinforce-b", "overtime-enemy");

            var first = ExtractionRaidMechanicsService.EvaluateTimeline(session, 1300, rule);
            var firstReplay = ExtractionRaidMechanicsService.EvaluateTimeline(session, 1300, rule);
            Assert.IsTrue(first.FirstMilestoneTriggered);
            Assert.IsFalse(firstReplay.FirstMilestoneTriggered);
            Assert.AreEqual(1, session.Content.TriggeredMilestoneIds.Count);

            var second = ExtractionRaidMechanicsService.EvaluateTimeline(session, 1600, rule);
            var overtime = ExtractionRaidMechanicsService.EvaluateTimeline(session, 1900, rule);
            var overtimeReplay = ExtractionRaidMechanicsService.EvaluateTimeline(session, 1900, rule);
            Assert.IsTrue(second.SecondMilestoneTriggered);
            Assert.IsTrue(overtime.EnteredOvertime);
            Assert.IsFalse(overtimeReplay.EnteredOvertime);
            Assert.IsTrue(session.Content.IsOvertime);
            Assert.AreEqual(3, session.Content.TriggeredMilestoneIds.Count);
        }

        [Test]
        public void LegacySessionSnapshot_EnsureInitializedAddsOnlyMissingCollections()
        {
            var session = CreateSession(900);
            session.Content = null;

            session.EnsureInitialized();

            Assert.IsNotNull(session.Content);
            Assert.IsNotNull(session.Content.UsedExtractionPointIds);
            Assert.IsNotNull(session.Content.TriggeredMilestoneIds);
            Assert.IsNotNull(session.Content.ActivatedGateIds);
            Assert.IsNotNull(session.Content.OpenedKeyDoorIds);
            Assert.IsNotNull(session.Content.GateRewardStates);
            Assert.IsFalse(session.Content.IsOvertime);
        }

        [Test]
        public void SnapshotMarks_AreIdempotentAndReuseCompatibleCollections()
        {
            var session = CreateSession(900);

            Assert.IsTrue(session.MarkExtractionPointUsed("exit"));
            Assert.IsFalse(session.MarkExtractionPointUsed("exit"));
            Assert.IsTrue(session.MarkGateActivated("gate"));
            Assert.IsFalse(session.MarkGateActivated("gate"));
            Assert.IsTrue(session.MarkKeyDoorOpened("door"));
            Assert.IsTrue(session.HasOpenedKeyDoor("door"));
            Assert.IsFalse(session.MarkKeyDoorOpened("door"));
            Assert.IsTrue(session.TryMarkGateRewarded("gate", "reward-spawn"));
            Assert.IsFalse(session.TryMarkGateRewarded("gate", "other-spawn"));
            Assert.IsTrue(session.TryGetGateRewardState("gate", out var reward));
            Assert.AreEqual("reward-spawn", reward.ContainerSpawnId);
            Assert.Contains("door", session.Content.OpenedKeyDoorIds);
        }

        [Test]
        public void GateModes_RejectMismatchedSacrificeAndCaptureFields()
        {
            var capture = new ExtractionGateDefinition(
                "gate-capture",
                "map-a",
                ExtractionGateMode.Capture,
                5,
                0,
                ExtractionItemRarity.Common,
                null,
                new[] { "spawn-a" },
                "encounter-a",
                10);
            Assert.IsTrue(capture.IsValid);

            capture.RequiredItemQuantity = 1;
            Assert.IsFalse(capture.IsValid);

            var sacrifice = new ExtractionGateDefinition(
                "gate-sacrifice",
                "map-a",
                ExtractionGateMode.Sacrifice,
                5,
                1,
                ExtractionItemRarity.Rare,
                "key-tag",
                new[] { "spawn-a" },
                null);
            Assert.IsTrue(sacrifice.IsValid);

            sacrifice.EnemyProfileId = "encounter-a";
            Assert.IsFalse(sacrifice.IsValid);

            var hold = new ExtractionGateDefinition(
                "gate-hold",
                "map-a",
                ExtractionGateMode.Hold,
                5,
                0,
                ExtractionItemRarity.Common,
                null,
                new[] { "spawn-a" },
                null,
                1);
            Assert.IsFalse(hold.IsValid);
        }

        [Test]
        public void Validator_MechanicsReferencesAndPointKindsAreFailClosed()
        {
            var config = CreateMechanicsConfig();
            config.EnemySpawnPoints.Add(new ExtractionEnemySpawnPointDefinition(
                "enemy-point",
                "map-a",
                ExtractionSpawnPointKind.Enemy));
            config.EnemyPointCandidates.Add(new ExtractionPointDifficultyCandidate(
                "enemy-point", 1, "enemy-profile", 1));

            var valid = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsTrue(valid.IsValid, valid.FirstError);

            config.EnemySpawnPoints.Add(new ExtractionEnemySpawnPointDefinition(
                "enemy-point",
                "map-a",
                ExtractionSpawnPointKind.Enemy));
            var duplicate = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(duplicate.IsValid);
            Assert.IsTrue(ContainsError(duplicate, "pointKind 重复"));

            config.EnemyPointCandidates[0].PointId = "missing-point";
            var missing = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(missing.IsValid);
            Assert.IsTrue(ContainsError(missing, "不存在的敌人/BOSS 点位"));
        }

        [Test]
        public void Validator_SpecialContainerPointRequiresSpecialContainer()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.ContainerSpawns[0].PointKind = ExtractionLootPointKind.Special;
            var special = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(special.IsValid);
            Assert.IsTrue(ContainsError(special, "IsSpecial"));
        }

        [Test]
        public void Validator_GatesDoorsAndTimelineProfilesRequireStableReferences()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.HostileExplorerEncounters.Add(new ExtractionHostileExplorerDefinition(
                "encounter-a", "map-a", "actor-a", "table-a", 0, 1));
            config.GateDefinitions.Add(new ExtractionGateDefinition(
                "gate-a",
                "map-a",
                ExtractionGateMode.Capture,
                5,
                0,
                ExtractionItemRarity.Common,
                null,
                new[] { "spawn-a" },
                "encounter-a",
                10));

            var valid = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsTrue(valid.IsValid, valid.FirstError);

            config.GateDefinitions[0].EnemyProfileId = "missing-encounter";
            var missingGateProfile = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(missingGateProfile.IsValid);
            Assert.IsTrue(ContainsError(missingGateProfile, "不存在的敌人 profile"));

            config = CreateMechanicsConfig();
            config.LockDefinitions.Add(new ExtractionLockDefinition("lock-a", "map-a"));
            config.KeyDoorDefinitions.Add(new ExtractionKeyDoorDefinition(
                "door-a", "map-a", "lock-a", "binding-a"));
            config.ReinforcementProfiles.Add(
                new ExtractionRaidReinforcementProfileDefinition("profile-a", 1f, 1f, 1f));
            config.HostileExplorerEncounters.Add(new ExtractionHostileExplorerDefinition(
                "overtime-a", "map-a", "actor-a", "table-a", 0, 1));
            config.RaidDifficultyRules.Add(
                new ExtractionRaidDifficultyRuleDefinition(
                    1, 600, 300, "profile-a", "profile-a", "overtime-a"));

            valid = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsTrue(valid.IsValid, valid.FirstError);

            config.KeyDoorDefinitions[0].LockDefinitionId = "missing-lock";
            var missingLock = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(missingLock.IsValid);
            Assert.IsTrue(ContainsError(missingLock, "不存在的锁定义"));

            config.KeyDoorDefinitions[0].LockDefinitionId = "lock-a";
            config.RaidDifficultyRules[0].FirstProfileId = "missing-profile";
            var missingProfile = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(missingProfile.IsValid);
            Assert.IsTrue(ContainsError(missingProfile, "不存在的第一层强化 profile"));

            config.RaidDifficultyRules[0].FirstProfileId = "profile-a";
            config.RaidDifficultyRules[0].OvertimeEntityId = "missing-overtime";
            var missingOvertime = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(missingOvertime.IsValid);
            Assert.IsTrue(ContainsError(missingOvertime, "不存在的 Overtime 敌人"));
        }

        [Test]
        public void Validator_ExtractionModesRejectInapplicableFields()
        {
            var config = CreateMechanicsConfig();
            var timed = new ExtractionPointDefinition(
                "timed",
                "map-a",
                5,
                true,
                0,
                true,
                false,
                ExtractionPointMode.Timed,
                30,
                0,
                0,
                0,
                ExtractionItemRarity.Common);
            config.ExtractionPoints.Add(timed);
            Assert.IsTrue(ExtractionLootContentConfigValidator.Validate(config).IsValid);

            timed.RequiredGateCount = 1;
            var invalid = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(invalid.IsValid);
        }

        [Test]
        public void Generator_DifficultyWithoutCandidateCanProduceEmptyManifest()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.LootProfiles[0].MinimumGeneratedDropsByRarity = new ExtractionRarityIntValues();
            foreach (var spawn in config.ContainerSpawns)
            {
                spawn.Candidates.Clear();
                spawn.Candidates.Add(new ExtractionWeightedContainerCandidate(
                    "container-a", 1, 2));
            }

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config,
                config.Maps[0],
                17,
                false,
                out var manifest,
                out var failure), failure.ToString());
            Assert.IsNotNull(manifest);
            Assert.IsEmpty(manifest.Containers);
        }

        [Test]
        public void Generator_DifficultyWithoutCandidateStillFailsRequiredGuarantee()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            foreach (var spawn in config.ContainerSpawns)
            {
                spawn.Candidates.Clear();
                spawn.Candidates.Add(new ExtractionWeightedContainerCandidate(
                    "container-a", 1, 2));
            }

            Assert.IsFalse(ExtractionRaidLootManifestGenerator.TryGenerate(
                config,
                config.Maps[0],
                17,
                false,
                out var manifest,
                out var failure));
            Assert.IsNull(manifest);
            Assert.AreEqual(ExtractionRaidLootManifestFailure.MissingRarityCandidate, failure);
        }

        [Test]
        public void Generator_SpecialSpawnDoesNotUseNormalContainer()
        {
            var config = ExtractionLootRuntimeFixture.CreateConfig();
            config.ContainerSpawns[0].PointKind = ExtractionLootPointKind.Special;

            Assert.IsTrue(ExtractionRaidLootManifestGenerator.TryGenerate(
                config,
                config.Maps[0],
                17,
                false,
                out var manifest,
                out var failure), failure.ToString());
            foreach (var container in manifest.Containers)
                Assert.AreNotEqual("spawn-a", container.ContainerId);
        }

        [Test]
        public void TimedBossAndSacrificeWindowsUseElapsedSnapshotAndAreIdempotent()
        {
            var session = CreateSession(900);
            var timed = new ExtractionPointDefinition(
                "timed",
                "map-a",
                5,
                true,
                0,
                true,
                false,
                ExtractionPointMode.Timed,
                120,
                0,
                0,
                0,
                ExtractionItemRarity.Common);
            Assert.IsFalse(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, timed, 1000));
            Assert.IsTrue(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, timed, 1780));
            Assert.IsFalse(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, timed, 1900));

            var boss = new ExtractionPointDefinition(
                "boss",
                "map-a",
                5,
                false,
                0,
                true,
                false,
                ExtractionPointMode.Boss,
                0,
                0,
                60,
                0,
                ExtractionItemRarity.Common)
            {
                RequiredRaidFlagId = "boss-open"
            };
            Assert.IsTrue(boss.IsValid);
            Assert.IsTrue(ExtractionRaidMechanicsService.TryOpenExtractionPoint(
                session, boss, 1000));
            Assert.IsFalse(ExtractionRaidMechanicsService.TryOpenExtractionPoint(
                session, boss, 1001));
            Assert.IsTrue(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, boss, 1059));
            Assert.IsFalse(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, boss, 1060));

            var sacrifice = new ExtractionPointDefinition(
                "sacrifice",
                "map-a",
                5,
                false,
                0,
                true,
                false,
                ExtractionPointMode.Sacrifice,
                0,
                0,
                30,
                1,
                ExtractionItemRarity.Rare);
            Assert.IsTrue(sacrifice.IsValid);
            Assert.IsTrue(ExtractionRaidMechanicsService.TryOpenExtractionPoint(
                session, sacrifice, 1100));
            Assert.IsTrue(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, sacrifice, 1129));
            Assert.IsFalse(ExtractionRaidMechanicsService.IsExtractionPointWindowOpen(
                session, sacrifice, 1130));
        }

        [Test]
        public void SnapshotRewardState_RoundTripsThroughJsonAndCannotBeRewritten()
        {
            var session = CreateSession(900);
            Assert.IsTrue(session.TryMarkGateRewarded("gate-a", "spawn-a"));

            var restoredContent = JsonUtility.FromJson<ExtractionActiveRaidContentState>(
                JsonUtility.ToJson(session.Content));
            var restored = CreateSession(900);
            restored.Content = restoredContent;
            restored.EnsureInitialized();

            Assert.IsTrue(restored.TryGetGateRewardState("gate-a", out var reward));
            Assert.AreEqual("spawn-a", reward.ContainerSpawnId);
            Assert.IsFalse(restored.TryMarkGateRewarded("gate-a", "spawn-b"));
        }

        [Test]
        public void OvertimeAllowsNormalExtractionAfterLegacyTimeout()
        {
            var session = CreateSession(10);
            var point = new ExtractionPointDefinition("exit", "map-a", 1, true, 0, true, false);

            Assert.IsFalse(ExtractionRaidPressureService.CanStartExtraction(
                session, point, 1010, false));
            session.Content.IsOvertime = true;
            Assert.IsTrue(ExtractionRaidPressureService.CanStartExtraction(
                session, point, 1010, false));
        }

        private static ExtractionPlayableConfig CreateMechanicsConfig()
        {
            var config = new ExtractionPlayableConfig(1, 1, 1, 1);
            config.Maps.Add(new ExtractionMapDefinition("map-a", "room-a", 900, 1, true));
            return config;
        }

        private static bool ContainsError(
            ExtractionLootContentValidationReport report,
            string text)
        {
            foreach (var error in report.Errors)
                if (error != null && error.Contains(text)) return true;
            return false;
        }

        private static ExtractionRaidSession CreateSession(int durationSeconds)
        {
            var map = new ExtractionMapDefinition("map-a", "room-a", durationSeconds, 1, true);
            return new ExtractionRaidSession(
                map,
                new ExtractionRaidStartRequest("raid-a", 42, 1000));
        }
    }
}
