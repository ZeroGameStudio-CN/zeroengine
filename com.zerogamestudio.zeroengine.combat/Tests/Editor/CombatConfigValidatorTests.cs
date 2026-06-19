using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AbilitySystem;
using ZeroEngine.Combat.Editor;
using ZeroEngine.Projectile;
using ZeroEngine.Spawner;
using Object = UnityEngine.Object;

namespace ZeroEngine.Combat.Editor.Tests
{
    public sealed class CombatConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingCombatConfigIssues()
        {
            var abilityA = ScriptableObject.CreateInstance<AbilityDataSO>();
            var abilityB = ScriptableObject.CreateInstance<AbilityDataSO>();
            var projectile = ScriptableObject.CreateInstance<ProjectileDataSO>();
            var spawnData = ScriptableObject.CreateInstance<SpawnDataSO>();

            try
            {
                abilityA.name = "AbilityA";
                abilityA.AbilityName = " slash ";
                abilityA.BaseCooldown = -1f;
                abilityA.MaxLevel = 0;
                abilityA.Triggers = new List<TriggerComponentData>
                {
                    null,
                    new ManualTriggerData { ButtonName = string.Empty },
                    new IntervalTriggerData { Interval = 0f },
                    new OnHitTriggerData { TriggerOnDealingDamage = false, TriggerOnTakingDamage = false }
                };
                abilityA.Conditions = new List<ConditionComponentData>
                {
                    new CooldownConditionData { CooldownSeconds = 0f },
                    new ResourceConditionData { RequiredAmount = 0 }
                };
                abilityA.Effects = new List<EffectComponentData>
                {
                    new DamageEffectData { DamageAmount = 0 },
                    new SpawnProjectileEffectData { ProjectilePrefab = null, Speed = 0f }
                };

                abilityB.name = "AbilityB";
                abilityB.AbilityName = "slash";

                projectile.name = "Projectile";
                projectile.ProjectileId = string.Empty;
                projectile.Prefab = null;
                projectile.Speed = 0f;
                projectile.MaxLifetime = 0f;
                projectile.TrajectoryType = TrajectoryType.Parabolic;
                projectile.LaunchAngle = 90f;
                projectile.CritChanceBonus = 1.5f;
                projectile.IsAOE = true;
                projectile.AOERadius = 0f;
                projectile.Scale = 0f;
                projectile.PoolPrewarmCount = -1;

                spawnData.name = "SpawnData";
                spawnData.SpawnId = "spawn_a";
                spawnData.SpawnInterval = 0f;
                spawnData.IntervalVariance = 1f;
                spawnData.PositionRandomRange = new Vector3(-1f, 0f, 0f);
                spawnData.Entries.Add(new SpawnEntry
                {
                    Prefab = null,
                    Weight = 0f,
                    SpawnCount = 1,
                    CountVariance = 1,
                    Scale = new Vector3(1f, 0f, 1f),
                    ScaleVariance = 1f
                });

                var issues = CombatConfigValidator.Validate(
                    new[] { abilityA, abilityB },
                    new[] { projectile },
                    new[] { spawnData });

                AssertIssue(issues, abilityA, CombatValidationSeverity.Warning, "AbilityName has leading/trailing whitespace.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "BaseCooldown must not be negative.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "MaxLevel must be greater than 0.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Trigger entry is empty.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Manual trigger ButtonName is empty.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Interval trigger Interval must be greater than 0.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "OnHit trigger has no enabled damage direction.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Cooldown condition seconds must be greater than 0.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Resource condition amount must be greater than 0.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Damage effect amount must be greater than 0.");
                AssertIssue(issues, abilityA, CombatValidationSeverity.Error, "Projectile effect is missing ProjectilePrefab.");
                Assert.That(issues.Count(issue => issue.Message.Contains("AbilityName") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "ProjectileId is empty.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Warning, "Projectile display name is empty.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "Projectile prefab is missing.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "Speed must be greater than 0.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "MaxLifetime must be greater than 0.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "LaunchAngle must be between 0 and 90 degrees.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "CritChanceBonus must be between 0 and 1.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "AOERadius must be greater than 0.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "Scale must be greater than 0.");
                AssertIssue(issues, projectile, CombatValidationSeverity.Error, "PoolPrewarmCount must not be negative.");

                AssertIssue(issues, spawnData, CombatValidationSeverity.Warning, "Spawn display name is empty.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Warning, "SpawnInterval is zero; this can spawn every frame.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "IntervalVariance must not be greater than SpawnInterval.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "PositionRandomRange must not contain negative components.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "Spawn entry prefab is missing.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "Spawn entry weight must be greater than 0.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "CountVariance must be lower than SpawnCount.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "Scale must be positive on every axis.");
                AssertIssue(issues, spawnData, CombatValidationSeverity.Error, "ScaleVariance must be lower than 1.");
            }
            finally
            {
                Object.DestroyImmediate(abilityA);
                Object.DestroyImmediate(abilityB);
                Object.DestroyImmediate(projectile);
                Object.DestroyImmediate(spawnData);
            }
        }

        private static void AssertIssue(
            IEnumerable<CombatValidationIssue> issues,
            ScriptableObject asset,
            CombatValidationSeverity severity,
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
