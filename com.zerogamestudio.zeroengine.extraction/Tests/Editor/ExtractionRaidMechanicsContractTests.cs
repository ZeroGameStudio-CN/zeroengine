using System.Collections.Generic;
using NUnit.Framework;

namespace POB.Extraction.Tests
{
    public sealed class ExtractionRaidMechanicsContractTests
    {
        [Test]
        public void RuleSnapshot_FreezesProfileRulesAndEffects()
        {
            var config = CreateRulesConfig();
            var map = config.Maps[0];

            Assert.IsTrue(ExtractionRaidMechanicsService.TryCreateRuleSnapshot(
                config, map, 1, out var snapshot));
            Assert.AreEqual(900, snapshot.DurationSeconds);
            Assert.AreEqual(2, snapshot.PhaseRules.Count);
            Assert.AreEqual(2, snapshot.Effects.Count);

            config.RaidEffects[0].Amount = 99f;
            Assert.AreEqual(1.25f, snapshot.Effects[0].Amount);
        }

        [Test]
        public void Timeline_DueRulesCommitOnceAndZeroEntersOvertime()
        {
            var config = CreateRulesConfig();
            ExtractionRaidMechanicsService.TryCreateRuleSnapshot(
                config, config.Maps[0], 1, out var snapshot);
            var session = CreateSession(config.Maps[0]);
            Assert.IsTrue(session.TrySetRuleSnapshot(snapshot));
            var due = new List<ExtractionRaidPhaseRuleDefinition>();

            Assert.IsTrue(ExtractionRaidMechanicsService.TryGetDuePhaseRules(session, 1300, due));
            Assert.AreEqual("phase-600", due[0].RuleId);
            Assert.IsTrue(ExtractionRaidMechanicsService.TryCommitPhaseRule(session, due[0]));
            Assert.IsFalse(ExtractionRaidMechanicsService.TryCommitPhaseRule(session, due[0]));

            Assert.IsTrue(ExtractionRaidMechanicsService.TryGetDuePhaseRules(session, 1900, due));
            Assert.AreEqual("phase-0", due[0].RuleId);
            Assert.IsTrue(ExtractionRaidMechanicsService.TryCommitPhaseRule(session, due[0]));
            Assert.AreEqual(ExtractionRaidPhase.Overtime, session.Content.Phase);
            Assert.IsTrue(session.Content.IsOvertime);
            Assert.IsFalse(ExtractionRaidPressureService.ShouldFailForTimeout(session, 1900));
        }

        [Test]
        public void EncounterSelection_UsesExistingEncounterPointAndDifficulty()
        {
            var config = CreateRulesConfig();
            config.HostileExplorerEncounters.Add(new ExtractionHostileExplorerDefinition(
                "encounter-a", "map-a", "actor-a", "loot-a", 0, 1)
            {
                SpawnPointId = "enemy-point",
                DifficultyLevel = 1
            });

            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectEncounter(
                config, "map-a", "enemy-point", 1, 17, out var selected));
            Assert.AreEqual("encounter-a", selected.EncounterId);
            Assert.IsFalse(ExtractionRaidMechanicsService.TrySelectEncounter(
                config, "map-a", "enemy-point", 2, 17, out _));
        }

        [Test]
        public void EncounterSelection_DoesNotUseBossReservedForExplicitEffects()
        {
            var config = CreateRulesConfig();
            config.HostileExplorerEncounters.Add(new ExtractionHostileExplorerDefinition(
                "regular", "map-a", "regular-actor", "loot-a", 0, 1)
            {
                SpawnPointId = "boss-point",
                DifficultyLevel = 1
            });

            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectEncounter(
                config, "map-a", "boss-point", 1, 17, out var selected));
            Assert.AreEqual("regular", selected.EncounterId);
        }

        [Test]
        public void RandomExit_ChoosesFarthestWithStableTie()
        {
            var distances = new Dictionary<string, float>
            {
                ["near"] = 1f,
                ["far-a"] = 5f,
                ["far-b"] = 5f
            };

            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectRandomExtractionPoint(
                distances, 42, out var first));
            Assert.IsTrue(ExtractionRaidMechanicsService.TrySelectRandomExtractionPoint(
                distances, 42, out var second));
            Assert.AreEqual(first, second);
            Assert.That(first, Is.EqualTo("far-a").Or.EqualTo("far-b"));
        }

        [Test]
        public void TimedExit_CutoffCompletesOnlyWhenChannelStartedInsideWindow()
        {
            var map = new ExtractionMapDefinition("map-a", "room-a", 900, 1, true);
            var session = CreateSession(map);
            var point = new ExtractionPointDefinition(
                "timed", "map-a", 10, false, 100, true, false)
            {
                Mode = ExtractionPointMode.Timed,
                OpenDurationSeconds = 30
            };

            Assert.IsTrue(ExtractionRaidMechanicsService.CanCompleteExtractionAtCutoff(
                session, point, 1120, 1130));
            Assert.IsFalse(ExtractionRaidMechanicsService.CanCompleteExtractionAtCutoff(
                session, point, 1090, 1130));
        }

        [Test]
        public void GateConfiguration_RejectsModeSpecificExtraFields()
        {
            var capture = new ExtractionLeverDefinition("gate", "map-a")
            {
                Mode = ExtractionGateMode.Capture,
                ChannelSeconds = 5,
                CaptureEncounterId = "encounter-a",
                EnemySpawnIntervalSeconds = 3
            };
            Assert.IsTrue(capture.IsGateConfigurationValid);

            capture.RequiredItemQuantity = 1;
            Assert.IsFalse(capture.IsGateConfigurationValid);
        }

        [Test]
        public void Validator_RejectsMissingTypedEffectTarget()
        {
            var config = CreateRulesConfig();
            config.RaidEffects[1].TargetId = "missing-encounter";

            var report = ExtractionLootContentConfigValidator.Validate(config);
            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("不存在的 Encounter", report.FirstError);
        }

        [Test]
        public void Validator_AllowsLegacyMapWithoutRuleProfileAlongsideOptInRules()
        {
            var config = CreateRulesConfig();
            config.Maps.Add(new ExtractionMapDefinition(
                "legacy-map",
                "legacy-room",
                600,
                1,
                true));

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.Errors.Exists(error => error.Contains("legacy-map")));
        }

        [Test]
        public void Validator_RejectsMapRuleReferenceWhenRuleTablesAreEmpty()
        {
            var config = new ExtractionPlayableConfig(1, 1, 1, 1);
            config.Maps.Add(new ExtractionMapDefinition(
                "configured-map",
                "configured-room",
                900,
                1,
                true)
            {
                RaidRuleProfileId = "missing-profile"
            });

            var report = ExtractionLootContentConfigValidator.Validate(config);

            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("missing-profile", report.FirstError);
        }

        private static ExtractionPlayableConfig CreateRulesConfig()
        {
            var config = new ExtractionPlayableConfig(1, 1, 1, 1);
            var map = new ExtractionMapDefinition("map-a", "room-a", 900, 1, true)
            {
                RaidRuleProfileId = "default"
            };
            config.Maps.Add(map);
            config.RaidRuleProfiles.Add(new ExtractionRaidRuleProfileDefinition("default", 900));
            config.RaidEffects.Add(new ExtractionRaidEffectDefinition(
                "reinforce", ExtractionRaidEffectType.EnemyStatMultiplier, "damage", 1.25f));
            config.RaidEffects.Add(new ExtractionRaidEffectDefinition(
                "overtime", ExtractionRaidEffectType.SpawnEncounter, "boss", 1f));
            config.RaidPhaseRules.Add(new ExtractionRaidPhaseRuleDefinition(
                "phase-600", "default", 1, 600, "reinforce"));
            config.RaidPhaseRules.Add(new ExtractionRaidPhaseRuleDefinition(
                "phase-0", "default", 1, 0, "overtime"));
            config.HostileExplorerEncounters.Add(new ExtractionHostileExplorerDefinition(
                "boss", "map-a", "boss-actor", "loot-a", 0, 1)
            {
                SpawnPointId = "boss-point",
                DifficultyLevel = 1,
                IsBoss = true
            });
            return config;
        }

        private static ExtractionRaidSession CreateSession(ExtractionMapDefinition map)
        {
            return new ExtractionRaidSession(
                map,
                new ExtractionRaidStartRequest("raid-a", 42, 1000));
        }
    }
}
