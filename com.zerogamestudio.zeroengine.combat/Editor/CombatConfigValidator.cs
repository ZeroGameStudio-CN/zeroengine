using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.AbilitySystem;
using ZeroEngine.Projectile;
using ZeroEngine.Spawner;

namespace ZeroEngine.Combat.Editor
{
    public enum CombatValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct CombatValidationIssue
    {
        public readonly ScriptableObject Asset;
        public readonly CombatValidationSeverity Severity;
        public readonly string FieldPath;
        public readonly string Message;

        public CombatValidationIssue(
            ScriptableObject asset,
            CombatValidationSeverity severity,
            string fieldPath,
            string message)
        {
            Asset = asset;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class CombatConfigValidator
    {
        public static IReadOnlyList<T> LoadAssets<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        public static IReadOnlyList<CombatValidationIssue> Validate(
            IEnumerable<AbilityDataSO> abilities = null,
            IEnumerable<ProjectileDataSO> projectiles = null,
            IEnumerable<SpawnDataSO> spawnData = null)
        {
            var issues = new List<CombatValidationIssue>();
            var abilityList = Materialize(abilities);
            var projectileList = Materialize(projectiles);
            var spawnList = Materialize(spawnData);

            foreach (var ability in abilityList)
            {
                ValidateAbility(ability, issues);
            }

            foreach (var projectile in projectileList)
            {
                ValidateProjectile(projectile, issues);
            }

            foreach (var spawn in spawnList)
            {
                ValidateSpawnData(spawn, issues);
            }

            AddDuplicateKeyIssues(abilityList, ability => ability.AbilityName, nameof(AbilityDataSO.AbilityName), "AbilityName", issues);
            AddDuplicateKeyIssues(projectileList, projectile => projectile.ProjectileId, nameof(ProjectileDataSO.ProjectileId), "ProjectileId", issues);
            AddDuplicateKeyIssues(spawnList, spawn => spawn.SpawnId, nameof(SpawnDataSO.SpawnId), "SpawnId", issues);
            return issues;
        }

        private static T[] Materialize<T>(IEnumerable<T> assets) where T : ScriptableObject
        {
            return (assets ?? Array.Empty<T>())
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void ValidateAbility(AbilityDataSO ability, ICollection<CombatValidationIssue> issues)
        {
            RequireId(ability, issues, nameof(AbilityDataSO.AbilityName), ability.AbilityName, "AbilityName");
            RequireDisplayName(ability, issues, nameof(AbilityDataSO.Description), ability.Description, "Ability description");
            RequireNonNegative(ability, issues, nameof(AbilityDataSO.CastTime), ability.CastTime, "CastTime");
            RequireNonNegative(ability, issues, nameof(AbilityDataSO.RecoveryTime), ability.RecoveryTime, "RecoveryTime");
            RequireNonNegative(ability, issues, nameof(AbilityDataSO.BaseCooldown), ability.BaseCooldown, "BaseCooldown");
            RequirePositive(ability, issues, nameof(AbilityDataSO.MaxLevel), ability.MaxLevel, "MaxLevel");
            RequireNonNegative(ability, issues, nameof(AbilityDataSO.EffectScalePerLevel), ability.EffectScalePerLevel, "EffectScalePerLevel");
            RequireNonNegative(ability, issues, nameof(AbilityDataSO.CooldownReductionPerLevel), ability.CooldownReductionPerLevel, "CooldownReductionPerLevel");

            if (ability.Triggers == null || ability.Triggers.Count == 0)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Warning, nameof(AbilityDataSO.Triggers), "Ability has no trigger components."));
            }
            else
            {
                for (var i = 0; i < ability.Triggers.Count; i++)
                {
                    ValidateTrigger(ability, ability.Triggers[i], $"{nameof(AbilityDataSO.Triggers)}[{i}]", issues);
                }
            }

            if (ability.Conditions != null)
            {
                for (var i = 0; i < ability.Conditions.Count; i++)
                {
                    ValidateCondition(ability, ability.Conditions[i], $"{nameof(AbilityDataSO.Conditions)}[{i}]", issues);
                }
            }

            if (ability.Effects == null || ability.Effects.Count == 0)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Warning, nameof(AbilityDataSO.Effects), "Ability has no effect components."));
            }
            else
            {
                for (var i = 0; i < ability.Effects.Count; i++)
                {
                    ValidateEffect(ability, ability.Effects[i], $"{nameof(AbilityDataSO.Effects)}[{i}]", issues);
                }
            }
        }

        private static void ValidateTrigger(
            AbilityDataSO ability,
            TriggerComponentData trigger,
            string fieldPath,
            ICollection<CombatValidationIssue> issues)
        {
            if (trigger == null)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, fieldPath, "Trigger entry is empty."));
                return;
            }

            if (trigger.TriggerMultipleTimes && trigger.TriggerTimes <= 0)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(TriggerComponentData.TriggerTimes)}", "TriggerTimes must be greater than 0 when TriggerMultipleTimes is enabled."));
            }

            switch (trigger)
            {
                case ManualTriggerData manual:
                    if (string.IsNullOrWhiteSpace(manual.ButtonName))
                    {
                        issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(ManualTriggerData.ButtonName)}", "Manual trigger ButtonName is empty."));
                    }
                    break;
                case IntervalTriggerData interval:
                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(IntervalTriggerData.Interval)}", interval.Interval, "Interval trigger Interval");
                    break;
                case OnHitTriggerData onHit:
                    if (!onHit.TriggerOnDealingDamage && !onHit.TriggerOnTakingDamage)
                    {
                        issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, fieldPath, "OnHit trigger has no enabled damage direction."));
                    }
                    break;
            }
        }

        private static void ValidateCondition(
            AbilityDataSO ability,
            ConditionComponentData condition,
            string fieldPath,
            ICollection<CombatValidationIssue> issues)
        {
            if (condition == null)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, fieldPath, "Condition entry is empty."));
                return;
            }

            switch (condition)
            {
                case CooldownConditionData cooldown:
                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(CooldownConditionData.CooldownSeconds)}", cooldown.CooldownSeconds, "Cooldown condition seconds");
                    break;
                case ResourceConditionData resource:
                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(ResourceConditionData.RequiredAmount)}", resource.RequiredAmount, "Resource condition amount");
                    break;
            }
        }

        private static void ValidateEffect(
            AbilityDataSO ability,
            EffectComponentData effect,
            string fieldPath,
            ICollection<CombatValidationIssue> issues)
        {
            if (effect == null)
            {
                issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, fieldPath, "Effect entry is empty."));
                return;
            }

            switch (effect)
            {
                case DamageEffectData damage:
                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(DamageEffectData.DamageAmount)}", damage.DamageAmount, "Damage effect amount");
                    break;
                case HealEffectData heal:
                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(HealEffectData.HealAmount)}", heal.HealAmount, "Heal effect amount");
                    break;
                case SpawnProjectileEffectData projectile:
                    if (projectile.ProjectilePrefab == null)
                    {
                        issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnProjectileEffectData.ProjectilePrefab)}", "Projectile effect is missing ProjectilePrefab."));
                    }

                    RequirePositive(ability, issues, $"{fieldPath}.{nameof(SpawnProjectileEffectData.Speed)}", projectile.Speed, "Projectile effect speed");
                    break;
                case ApplyBuffEffectData buff:
                    if (buff.BuffToApply == null)
                    {
                        issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(ApplyBuffEffectData.BuffToApply)}", "Buff effect is missing BuffToApply."));
                    }

                    if (buff.DurationOverride < 0f && !Mathf.Approximately(buff.DurationOverride, -1f))
                    {
                        issues.Add(new CombatValidationIssue(ability, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(ApplyBuffEffectData.DurationOverride)}", "DurationOverride must be -1 or greater than or equal to 0."));
                    }
                    break;
            }
        }

        private static void ValidateProjectile(ProjectileDataSO projectile, ICollection<CombatValidationIssue> issues)
        {
            RequireId(projectile, issues, nameof(ProjectileDataSO.ProjectileId), projectile.ProjectileId, "ProjectileId");
            RequireDisplayName(projectile, issues, nameof(ProjectileDataSO.DisplayName), projectile.DisplayName, "Projectile display name");

            if (projectile.Prefab == null)
            {
                issues.Add(new CombatValidationIssue(projectile, CombatValidationSeverity.Error, nameof(ProjectileDataSO.Prefab), "Projectile prefab is missing."));
            }

            RequirePositive(projectile, issues, nameof(ProjectileDataSO.Speed), projectile.Speed, "Speed");
            RequirePositive(projectile, issues, nameof(ProjectileDataSO.MaxLifetime), projectile.MaxLifetime, "MaxLifetime");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.MaxDistance), projectile.MaxDistance, "MaxDistance");
            RequirePositive(projectile, issues, nameof(ProjectileDataSO.HomingTurnSpeed), projectile.HomingTurnSpeed, "HomingTurnSpeed");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.HomingDelay), projectile.HomingDelay, "HomingDelay");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.CurveHeightVariance), projectile.CurveHeightVariance, "CurveHeightVariance");
            RequirePositive(projectile, issues, nameof(ProjectileDataSO.CollisionRadius), projectile.CollisionRadius, "CollisionRadius");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.PierceCount), projectile.PierceCount, "PierceCount");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.BounceCount), projectile.BounceCount, "BounceCount");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.BaseDamage), projectile.BaseDamage, "BaseDamage");
            RequireNormalized(projectile, issues, nameof(ProjectileDataSO.CritChanceBonus), projectile.CritChanceBonus, "CritChanceBonus");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.CritDamageBonus), projectile.CritDamageBonus, "CritDamageBonus");
            RequirePositive(projectile, issues, nameof(ProjectileDataSO.Scale), projectile.Scale, "Scale");
            RequireNonNegative(projectile, issues, nameof(ProjectileDataSO.PoolPrewarmCount), projectile.PoolPrewarmCount, "PoolPrewarmCount");

            if (projectile.TrajectoryType == TrajectoryType.Parabolic && (projectile.LaunchAngle <= 0f || projectile.LaunchAngle >= 90f))
            {
                issues.Add(new CombatValidationIssue(projectile, CombatValidationSeverity.Error, nameof(ProjectileDataSO.LaunchAngle), "LaunchAngle must be between 0 and 90 degrees."));
            }

            if (projectile.IsAOE)
            {
                RequirePositive(projectile, issues, nameof(ProjectileDataSO.AOERadius), projectile.AOERadius, "AOERadius");
                if (projectile.AOEDamageFalloff == null || projectile.AOEDamageFalloff.length == 0)
                {
                    issues.Add(new CombatValidationIssue(projectile, CombatValidationSeverity.Error, nameof(ProjectileDataSO.AOEDamageFalloff), "AOEDamageFalloff is empty while IsAOE is enabled."));
                }
            }
        }

        private static void ValidateSpawnData(SpawnDataSO spawnData, ICollection<CombatValidationIssue> issues)
        {
            RequireId(spawnData, issues, nameof(SpawnDataSO.SpawnId), spawnData.SpawnId, "SpawnId");
            RequireDisplayName(spawnData, issues, nameof(SpawnDataSO.DisplayName), spawnData.DisplayName, "Spawn display name");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.MaxActiveCount), spawnData.MaxActiveCount, "MaxActiveCount");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.TotalSpawnLimit), spawnData.TotalSpawnLimit, "TotalSpawnLimit");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.SpawnInterval), spawnData.SpawnInterval, "SpawnInterval");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.IntervalVariance), spawnData.IntervalVariance, "IntervalVariance");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.InitialDelay), spawnData.InitialDelay, "InitialDelay");
            RequireNonNegative(spawnData, issues, nameof(SpawnDataSO.PoolWarmupSize), spawnData.PoolWarmupSize, "PoolWarmupSize");

            if (Mathf.Approximately(spawnData.SpawnInterval, 0f))
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Warning, nameof(SpawnDataSO.SpawnInterval), "SpawnInterval is zero; this can spawn every frame."));
            }

            if (spawnData.IntervalVariance > spawnData.SpawnInterval)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, nameof(SpawnDataSO.IntervalVariance), "IntervalVariance must not be greater than SpawnInterval."));
            }

            if (HasNegativeComponent(spawnData.PositionRandomRange))
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, nameof(SpawnDataSO.PositionRandomRange), "PositionRandomRange must not contain negative components."));
            }

            if (spawnData.Entries == null || spawnData.Entries.Count == 0)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, nameof(SpawnDataSO.Entries), "Spawn entries are empty."));
                return;
            }

            for (var i = 0; i < spawnData.Entries.Count; i++)
            {
                ValidateSpawnEntry(spawnData, spawnData.Entries[i], $"{nameof(SpawnDataSO.Entries)}[{i}]", issues);
            }
        }

        private static void ValidateSpawnEntry(
            SpawnDataSO spawnData,
            SpawnEntry entry,
            string fieldPath,
            ICollection<CombatValidationIssue> issues)
        {
            if (entry == null)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, fieldPath, "Spawn entry is empty."));
                return;
            }

            if (!entry.IsEnabled)
            {
                return;
            }

            if (entry.Prefab == null)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnEntry.Prefab)}", "Spawn entry prefab is missing."));
            }

            RequirePositive(spawnData, issues, $"{fieldPath}.{nameof(SpawnEntry.Weight)}", entry.Weight, "Spawn entry weight");
            RequirePositive(spawnData, issues, $"{fieldPath}.{nameof(SpawnEntry.SpawnCount)}", entry.SpawnCount, "Spawn count");
            RequireNonNegative(spawnData, issues, $"{fieldPath}.{nameof(SpawnEntry.CountVariance)}", entry.CountVariance, "CountVariance");
            RequireNonNegative(spawnData, issues, $"{fieldPath}.{nameof(SpawnEntry.ScaleVariance)}", entry.ScaleVariance, "ScaleVariance");

            if (entry.CountVariance >= entry.SpawnCount)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnEntry.CountVariance)}", "CountVariance must be lower than SpawnCount."));
            }

            if (entry.Scale.x <= 0f || entry.Scale.y <= 0f || entry.Scale.z <= 0f)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnEntry.Scale)}", "Scale must be positive on every axis."));
            }

            if (entry.ScaleVariance >= 1f)
            {
                issues.Add(new CombatValidationIssue(spawnData, CombatValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnEntry.ScaleVariance)}", "ScaleVariance must be lower than 1."));
            }
        }

        private static bool HasNegativeComponent(Vector3 value)
        {
            return value.x < 0f || value.y < 0f || value.z < 0f;
        }

        private static void RequireId(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} is empty."));
            }
            else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace."));
            }
        }

        private static void RequireDisplayName(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Warning, fieldPath, $"{label} is empty."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value <= 0f)
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value < 0)
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f)
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNormalized(
            ScriptableObject asset,
            ICollection<CombatValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value > 1f)
            {
                issues.Add(new CombatValidationIssue(asset, CombatValidationSeverity.Error, fieldPath, $"{label} must be between 0 and 1."));
            }
        }

        private static void AddDuplicateKeyIssues<T>(
            IEnumerable<T> assets,
            Func<T, string> keySelector,
            string fieldPath,
            string label,
            ICollection<CombatValidationIssue> issues)
            where T : ScriptableObject
        {
            foreach (var duplicateGroup in assets
                         .Select(asset => new { Asset = asset, Key = keySelector(asset)?.Trim() })
                         .Where(record => !string.IsNullOrEmpty(record.Key))
                         .GroupBy(record => record.Key, StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1)
                {
                    continue;
                }

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new CombatValidationIssue(
                        duplicate.Asset,
                        CombatValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicate.Key}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }
    }
}
