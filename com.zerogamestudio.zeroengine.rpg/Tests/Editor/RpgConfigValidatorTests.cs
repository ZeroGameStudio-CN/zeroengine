using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.RPG.BattleReward;
using ZeroEngine.RPG.Editor;
using ZeroEngine.RPG.Encounter;
using ZeroEngine.RPG.SkillVisual;
using Object = UnityEngine.Object;

namespace ZeroEngine.RPG.Editor.Tests
{
    public sealed class RpgConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingRpgConfigIssues()
        {
            var battleReward = ScriptableObject.CreateInstance<BattleRewardConfigSO>();
            var encounterA = ScriptableObject.CreateInstance<EncounterTableSO>();
            var encounterB = ScriptableObject.CreateInstance<EncounterTableSO>();
            var skillVisual = ScriptableObject.CreateInstance<SkillVisualDataSO>();

            try
            {
                battleReward.name = "BattleReward";
                battleReward.LevelPenaltyPerLevel = -0.1f;
                battleReward.MinExpRatio = 0.8f;
                battleReward.MaxExpRatio = 0.5f;
                battleReward.SecondaryJobJPRatio = 1.5f;
                battleReward.GoldMultiplier = 0f;
                battleReward.EliteExpMultiplier = 0f;
                battleReward.BossExpMultiplier = 0f;
                battleReward.NoDamageExpBonus = 0f;

                encounterA.name = "EncounterA";
                encounterA.TableId = " forest ";
                encounterA.DisplayName = string.Empty;
                encounterA.LevelRange = new Vector2Int(10, 1);
                encounterA.BaseEncounterRate = 0.8f;
                encounterA.MaxEncounterRate = 0.5f;
                encounterA.RatePerStep = -0.1f;
                encounterA.CooldownSteps = -1;
                encounterA.EliteChance = 1.5f;
                encounterA.EliteMinSteps = -1;
                encounterA.NormalEntries = new List<EncounterEntry>
                {
                    null,
                    new EncounterEntry
                    {
                        EntryId = "entry_a",
                        EnemyIds = new List<string> { string.Empty },
                        MinCount = 3,
                        MaxCount = 1,
                        Weight = 0f,
                        MinPlayerLevel = 5,
                        MaxPlayerLevel = 1
                    }
                };
                encounterA.EliteEntries = new List<EncounterEntry>
                {
                    new EncounterEntry
                    {
                        EntryId = "entry_a",
                        EnemyIds = new List<string> { "elite" },
                        MinCount = 1,
                        MaxCount = 1,
                        Weight = 1f,
                        IsElite = false
                    }
                };
                encounterA.BossEntries = new List<EncounterEntry>
                {
                    new EncounterEntry
                    {
                        EntryId = "boss_a",
                        EnemyIds = new List<string> { "boss" },
                        MinCount = 1,
                        MaxCount = 1,
                        Weight = 1f,
                        IsBoss = false
                    }
                };

                encounterB.name = "EncounterB";
                encounterB.TableId = "forest";
                encounterB.DisplayName = "Forest";

                skillVisual.name = "SkillVisual";
                skillVisual.DisplayName = " slash ";
                skillVisual.Description = string.Empty;
                skillVisual.TotalDuration = 0.5f;
                skillVisual.Events = new List<VisualEvent>
                {
                    null,
                    new PlayAnimationEvent
                    {
                        Delay = -1f,
                        ParameterName = string.Empty,
                        TransitionDuration = -0.1f,
                        Layer = -1
                    },
                    new SpawnVFXEvent
                    {
                        Delay = 1f,
                        VFXPrefab = null,
                        Lifetime = -1f,
                        Scale = 0f
                    },
                    new PlaySoundEvent
                    {
                        AudioClip = null,
                        Volume = 2f,
                        Pitch = 0f,
                        PitchVariation = 1f
                    },
                    new DamagePopupEvent
                    {
                        PopupType = PopupType.Custom,
                        CustomText = string.Empty,
                        PopupPrefab = null,
                        Duration = 0f,
                        FloatDistance = -1f,
                        StartScale = 0f,
                        MaxScale = 0f
                    },
                    new MoveEvent
                    {
                        Duration = 0f,
                        StopDistance = -1f
                    },
                    new CameraControlEvent
                    {
                        Action = CameraAction.Shake,
                        ShakeDuration = 0f,
                        ShakeStrength = -1f,
                        ShakeVibrato = 0
                    }
                };

                var issues = RpgConfigValidator.Validate(
                    new[] { battleReward },
                    new[] { encounterA, encounterB },
                    new[] { skillVisual });

                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "LevelPenaltyPerLevel must be between 0 and 1.");
                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "MaxExpRatio must be greater than or equal to 1.");
                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "MaxExpRatio must not be lower than MinExpRatio.");
                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "SecondaryJobJPRatio must be between 0 and 1.");
                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "GoldMultiplier must be greater than 0.");
                AssertIssue(issues, battleReward, RpgValidationSeverity.Error, "EliteExpMultiplier must be greater than or equal to 1.");

                AssertIssue(issues, encounterA, RpgValidationSeverity.Warning, "Encounter table ID has leading/trailing whitespace.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Warning, "Encounter table display name is empty.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "LevelRange maximum is lower than minimum.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "RatePerStep must not be negative.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "MaxEncounterRate must not be lower than BaseEncounterRate.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "Encounter entry is empty.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "Encounter enemy ID is empty.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "Encounter MaxCount is lower than MinCount.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "Encounter weight must be greater than 0.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Error, "MaxPlayerLevel is lower than MinPlayerLevel.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Warning, "Elite entry is not marked IsElite.");
                AssertIssue(issues, encounterA, RpgValidationSeverity.Warning, "Boss entry is not marked IsBoss.");
                Assert.That(issues.Count(issue => issue.Message.Contains("Encounter table ID") && issue.Message.Contains("duplicated")), Is.EqualTo(2));
                Assert.That(issues.Count(issue => issue.Message.Contains("Encounter entry ID") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, skillVisual, RpgValidationSeverity.Warning, "Skill visual display name has leading/trailing whitespace.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Warning, "Skill visual description is empty.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "TotalDuration is shorter than the latest enabled event delay.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Visual event is empty.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Visual event Delay must not be negative.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Animation ParameterName is empty.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "VFXPrefab is missing.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Sound Volume must be between 0 and 1.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Custom popup text is empty.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Move Duration must be greater than 0.");
                AssertIssue(issues, skillVisual, RpgValidationSeverity.Error, "Camera ShakeVibrato must be greater than 0.");
            }
            finally
            {
                Object.DestroyImmediate(battleReward);
                Object.DestroyImmediate(encounterA);
                Object.DestroyImmediate(encounterB);
                Object.DestroyImmediate(skillVisual);
            }
        }

        private static void AssertIssue(
            IEnumerable<RpgValidationIssue> issues,
            ScriptableObject asset,
            RpgValidationSeverity severity,
            string message)
        {
            Assert.That(
                issues.Any(issue =>
                    issue.Asset == asset &&
                    issue.Severity == severity &&
                    issue.Message == message),
                Is.True,
                message);
        }
    }
}
