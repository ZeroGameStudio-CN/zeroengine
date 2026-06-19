using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.RPG.BattleReward;
using ZeroEngine.RPG.Encounter;
using ZeroEngine.RPG.SkillVisual;

namespace ZeroEngine.RPG.Editor
{
    public enum RpgValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct RpgValidationIssue
    {
        public readonly ScriptableObject Asset;
        public readonly RpgValidationSeverity Severity;
        public readonly string FieldPath;
        public readonly string Message;

        public RpgValidationIssue(
            ScriptableObject asset,
            RpgValidationSeverity severity,
            string fieldPath,
            string message)
        {
            Asset = asset;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class RpgConfigValidator
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

        public static IReadOnlyList<RpgValidationIssue> Validate(
            IEnumerable<BattleRewardConfigSO> battleRewards = null,
            IEnumerable<EncounterTableSO> encounterTables = null,
            IEnumerable<SkillVisualDataSO> skillVisuals = null)
        {
            var issues = new List<RpgValidationIssue>();
            var battleRewardList = Materialize(battleRewards);
            var encounterTableList = Materialize(encounterTables);
            var skillVisualList = Materialize(skillVisuals);

            foreach (var config in battleRewardList)
            {
                ValidateBattleReward(config, issues);
            }

            foreach (var table in encounterTableList)
            {
                ValidateEncounterTable(table, issues);
            }

            foreach (var skillVisual in skillVisualList)
            {
                ValidateSkillVisual(skillVisual, issues);
            }

            AddDuplicateStringIssues(encounterTableList, table => table.TableId, nameof(EncounterTableSO.TableId), "Encounter table ID", issues);
            AddDuplicateStringIssues(skillVisualList, skill => skill.DisplayName, nameof(SkillVisualDataSO.DisplayName), "Skill visual display name", issues);
            return issues;
        }

        private static T[] Materialize<T>(IEnumerable<T> assets) where T : ScriptableObject
        {
            return (assets ?? Array.Empty<T>())
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void ValidateBattleReward(BattleRewardConfigSO config, ICollection<RpgValidationIssue> issues)
        {
            RequireNormalized(config, issues, nameof(BattleRewardConfigSO.LevelPenaltyPerLevel), config.LevelPenaltyPerLevel, "LevelPenaltyPerLevel");
            RequireNormalized(config, issues, nameof(BattleRewardConfigSO.MinExpRatio), config.MinExpRatio, "MinExpRatio");
            RequireNormalized(config, issues, nameof(BattleRewardConfigSO.LevelBonusPerLevel), config.LevelBonusPerLevel, "LevelBonusPerLevel");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.MaxExpRatio), config.MaxExpRatio, 1f, "MaxExpRatio");
            RequireNormalized(config, issues, nameof(BattleRewardConfigSO.SecondaryJobJPRatio), config.SecondaryJobJPRatio, "SecondaryJobJPRatio");
            RequirePositive(config, issues, nameof(BattleRewardConfigSO.GoldMultiplier), config.GoldMultiplier, "GoldMultiplier");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.EliteExpMultiplier), config.EliteExpMultiplier, 1f, "EliteExpMultiplier");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.EliteGoldMultiplier), config.EliteGoldMultiplier, 1f, "EliteGoldMultiplier");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.BossExpMultiplier), config.BossExpMultiplier, 1f, "BossExpMultiplier");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.BossGoldMultiplier), config.BossGoldMultiplier, 1f, "BossGoldMultiplier");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.NoDamageExpBonus), config.NoDamageExpBonus, 1f, "NoDamageExpBonus");
            RequireAtLeast(config, issues, nameof(BattleRewardConfigSO.FullClearGoldBonus), config.FullClearGoldBonus, 1f, "FullClearGoldBonus");

            if (config.MaxExpRatio < config.MinExpRatio)
            {
                issues.Add(new RpgValidationIssue(config, RpgValidationSeverity.Error, nameof(BattleRewardConfigSO.MaxExpRatio), "MaxExpRatio must not be lower than MinExpRatio."));
            }
        }

        private static void ValidateEncounterTable(EncounterTableSO table, ICollection<RpgValidationIssue> issues)
        {
            RequireId(table, issues, nameof(EncounterTableSO.TableId), table.TableId, "Encounter table ID");
            RequireDisplayName(table, issues, nameof(EncounterTableSO.DisplayName), table.DisplayName, "Encounter table display name");
            RequirePositive(table, issues, $"{nameof(EncounterTableSO.LevelRange)}.x", table.LevelRange.x, "LevelRange minimum");
            RequirePositive(table, issues, $"{nameof(EncounterTableSO.LevelRange)}.y", table.LevelRange.y, "LevelRange maximum");
            RequireNormalized(table, issues, nameof(EncounterTableSO.BaseEncounterRate), table.BaseEncounterRate, "BaseEncounterRate");
            RequireNonNegative(table, issues, nameof(EncounterTableSO.RatePerStep), table.RatePerStep, "RatePerStep");
            RequireNormalized(table, issues, nameof(EncounterTableSO.MaxEncounterRate), table.MaxEncounterRate, "MaxEncounterRate");
            RequireNonNegative(table, issues, nameof(EncounterTableSO.CooldownSteps), table.CooldownSteps, "CooldownSteps");
            RequireNormalized(table, issues, nameof(EncounterTableSO.EliteChance), table.EliteChance, "EliteChance");
            RequireNonNegative(table, issues, nameof(EncounterTableSO.EliteMinSteps), table.EliteMinSteps, "EliteMinSteps");

            if (table.LevelRange.y < table.LevelRange.x)
            {
                issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, nameof(EncounterTableSO.LevelRange), "LevelRange maximum is lower than minimum."));
            }

            if (table.MaxEncounterRate < table.BaseEncounterRate)
            {
                issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, nameof(EncounterTableSO.MaxEncounterRate), "MaxEncounterRate must not be lower than BaseEncounterRate."));
            }

            if (table.NormalEntries == null || table.NormalEntries.Count == 0)
            {
                issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Warning, nameof(EncounterTableSO.NormalEntries), "Encounter table has no normal entries."));
            }

            ValidateEncounterEntries(table, table.NormalEntries, nameof(EncounterTableSO.NormalEntries), false, false, issues);
            ValidateEncounterEntries(table, table.EliteEntries, nameof(EncounterTableSO.EliteEntries), true, false, issues);
            ValidateEncounterEntries(table, table.BossEntries, nameof(EncounterTableSO.BossEntries), false, true, issues);
            AddDuplicateEncounterEntryIssues(table, issues);
        }

        private static void ValidateEncounterEntries(
            EncounterTableSO table,
            IReadOnlyList<EncounterEntry> entries,
            string listPath,
            bool shouldBeElite,
            bool shouldBeBoss,
            ICollection<RpgValidationIssue> issues)
        {
            if (entries == null)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var fieldPath = $"{listPath}[{i}]";
                if (entry == null)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, fieldPath, "Encounter entry is empty."));
                    continue;
                }

                RequireId(table, issues, $"{fieldPath}.{nameof(EncounterEntry.EntryId)}", entry.EntryId, "Encounter entry ID");
                if (entry.EnemyIds == null || entry.EnemyIds.Count == 0)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(EncounterEntry.EnemyIds)}", "Encounter entry has no enemy IDs."));
                }
                else
                {
                    for (var enemyIndex = 0; enemyIndex < entry.EnemyIds.Count; enemyIndex++)
                    {
                        if (string.IsNullOrWhiteSpace(entry.EnemyIds[enemyIndex]))
                        {
                            issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(EncounterEntry.EnemyIds)}[{enemyIndex}]", "Encounter enemy ID is empty."));
                        }
                    }
                }

                RequirePositive(table, issues, $"{fieldPath}.{nameof(EncounterEntry.MinCount)}", entry.MinCount, "Encounter MinCount");
                RequirePositive(table, issues, $"{fieldPath}.{nameof(EncounterEntry.MaxCount)}", entry.MaxCount, "Encounter MaxCount");
                RequirePositive(table, issues, $"{fieldPath}.{nameof(EncounterEntry.Weight)}", entry.Weight, "Encounter weight");
                RequirePositive(table, issues, $"{fieldPath}.{nameof(EncounterEntry.MinPlayerLevel)}", entry.MinPlayerLevel, "MinPlayerLevel");
                RequireNonNegative(table, issues, $"{fieldPath}.{nameof(EncounterEntry.MaxPlayerLevel)}", entry.MaxPlayerLevel, "MaxPlayerLevel");

                if (entry.MaxCount < entry.MinCount)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(EncounterEntry.MaxCount)}", "Encounter MaxCount is lower than MinCount."));
                }

                if (entry.MaxPlayerLevel > 0 && entry.MaxPlayerLevel < entry.MinPlayerLevel)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(EncounterEntry.MaxPlayerLevel)}", "MaxPlayerLevel is lower than MinPlayerLevel."));
                }

                if (shouldBeElite && !entry.IsElite)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Warning, $"{fieldPath}.{nameof(EncounterEntry.IsElite)}", "Elite entry is not marked IsElite."));
                }

                if (shouldBeBoss && !entry.IsBoss)
                {
                    issues.Add(new RpgValidationIssue(table, RpgValidationSeverity.Warning, $"{fieldPath}.{nameof(EncounterEntry.IsBoss)}", "Boss entry is not marked IsBoss."));
                }
            }
        }

        private static void AddDuplicateEncounterEntryIssues(EncounterTableSO table, ICollection<RpgValidationIssue> issues)
        {
            var records = new List<(EncounterEntry Entry, string FieldPath)>();
            AddEncounterRecords(records, table.NormalEntries, nameof(EncounterTableSO.NormalEntries));
            AddEncounterRecords(records, table.EliteEntries, nameof(EncounterTableSO.EliteEntries));
            AddEncounterRecords(records, table.BossEntries, nameof(EncounterTableSO.BossEntries));

            foreach (var duplicateGroup in records
                         .Where(record => record.Entry != null && !string.IsNullOrWhiteSpace(record.Entry.EntryId))
                         .GroupBy(record => record.Entry.EntryId.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1)
                {
                    continue;
                }

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new RpgValidationIssue(
                        table,
                        RpgValidationSeverity.Error,
                        $"{duplicate.FieldPath}.{nameof(EncounterEntry.EntryId)}",
                        $"Encounter entry ID '{duplicateGroup.Key}' is duplicated in {duplicates.Length} entries."));
                }
            }
        }

        private static void AddEncounterRecords(
            ICollection<(EncounterEntry Entry, string FieldPath)> records,
            IReadOnlyList<EncounterEntry> entries,
            string listPath)
        {
            if (entries == null)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                records.Add((entries[i], $"{listPath}[{i}]"));
            }
        }

        private static void ValidateSkillVisual(SkillVisualDataSO skillVisual, ICollection<RpgValidationIssue> issues)
        {
            RequireId(skillVisual, issues, nameof(SkillVisualDataSO.DisplayName), skillVisual.DisplayName, "Skill visual display name");
            RequireDisplayName(skillVisual, issues, nameof(SkillVisualDataSO.Description), skillVisual.Description, "Skill visual description");
            RequireNonNegative(skillVisual, issues, nameof(SkillVisualDataSO.TotalDuration), skillVisual.TotalDuration, "TotalDuration");

            if (skillVisual.Events == null || skillVisual.Events.Count == 0)
            {
                issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Warning, nameof(SkillVisualDataSO.Events), "Skill visual has no events."));
                return;
            }

            var maxDelay = 0f;
            for (var i = 0; i < skillVisual.Events.Count; i++)
            {
                var fieldPath = $"{nameof(SkillVisualDataSO.Events)}[{i}]";
                var visualEvent = skillVisual.Events[i];
                if (visualEvent == null)
                {
                    issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, fieldPath, "Visual event is empty."));
                    continue;
                }

                if (visualEvent.Enabled)
                {
                    maxDelay = Mathf.Max(maxDelay, visualEvent.Delay);
                }

                ValidateVisualEvent(skillVisual, visualEvent, fieldPath, issues);
            }

            if (skillVisual.TotalDuration > 0f && skillVisual.TotalDuration < maxDelay)
            {
                issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, nameof(SkillVisualDataSO.TotalDuration), "TotalDuration is shorter than the latest enabled event delay."));
            }
        }

        private static void ValidateVisualEvent(
            SkillVisualDataSO skillVisual,
            VisualEvent visualEvent,
            string fieldPath,
            ICollection<RpgValidationIssue> issues)
        {
            RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(VisualEvent.Delay)}", visualEvent.Delay, "Visual event Delay");

            switch (visualEvent)
            {
                case PlayAnimationEvent animation:
                    if (string.IsNullOrWhiteSpace(animation.ParameterName))
                    {
                        issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(PlayAnimationEvent.ParameterName)}", "Animation ParameterName is empty."));
                    }

                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(PlayAnimationEvent.TransitionDuration)}", animation.TransitionDuration, "Animation TransitionDuration");
                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(PlayAnimationEvent.Layer)}", animation.Layer, "Animation Layer");
                    break;
                case SpawnVFXEvent vfx:
                    if (vfx.VFXPrefab == null)
                    {
                        issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(SpawnVFXEvent.VFXPrefab)}", "VFXPrefab is missing."));
                    }

                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(SpawnVFXEvent.Lifetime)}", vfx.Lifetime, "VFX Lifetime");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(SpawnVFXEvent.Scale)}", vfx.Scale, "VFX Scale");
                    break;
                case PlaySoundEvent sound:
                    if (sound.AudioClip == null)
                    {
                        issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(PlaySoundEvent.AudioClip)}", "AudioClip is missing."));
                    }

                    RequireNormalized(skillVisual, issues, $"{fieldPath}.{nameof(PlaySoundEvent.Volume)}", sound.Volume, "Sound Volume");
                    RequireRange(skillVisual, issues, $"{fieldPath}.{nameof(PlaySoundEvent.Pitch)}", sound.Pitch, 0.5f, 2f, "Sound Pitch");
                    RequireRange(skillVisual, issues, $"{fieldPath}.{nameof(PlaySoundEvent.PitchVariation)}", sound.PitchVariation, 0f, 0.5f, "Sound PitchVariation");
                    break;
                case DamagePopupEvent popup:
                    if (popup.PopupPrefab == null)
                    {
                        issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(DamagePopupEvent.PopupPrefab)}", "PopupPrefab is missing."));
                    }

                    if (popup.PopupType == PopupType.Custom && string.IsNullOrWhiteSpace(popup.CustomText))
                    {
                        issues.Add(new RpgValidationIssue(skillVisual, RpgValidationSeverity.Error, $"{fieldPath}.{nameof(DamagePopupEvent.CustomText)}", "Custom popup text is empty."));
                    }

                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(DamagePopupEvent.Duration)}", popup.Duration, "Popup Duration");
                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(DamagePopupEvent.FloatDistance)}", popup.FloatDistance, "Popup FloatDistance");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(DamagePopupEvent.StartScale)}", popup.StartScale, "Popup StartScale");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(DamagePopupEvent.MaxScale)}", popup.MaxScale, "Popup MaxScale");
                    break;
                case MoveEvent move:
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(MoveEvent.Duration)}", move.Duration, "Move Duration");
                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(MoveEvent.StopDistance)}", move.StopDistance, "Move StopDistance");
                    break;
                case CameraControlEvent camera:
                    ValidateCameraEvent(skillVisual, camera, fieldPath, issues);
                    break;
            }
        }

        private static void ValidateCameraEvent(
            SkillVisualDataSO skillVisual,
            CameraControlEvent camera,
            string fieldPath,
            ICollection<RpgValidationIssue> issues)
        {
            switch (camera.Action)
            {
                case CameraAction.Shake:
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.ShakeDuration)}", camera.ShakeDuration, "Camera ShakeDuration");
                    RequireNonNegative(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.ShakeStrength)}", camera.ShakeStrength, "Camera ShakeStrength");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.ShakeVibrato)}", camera.ShakeVibrato, "Camera ShakeVibrato");
                    break;
                case CameraAction.Zoom:
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.ZoomValue)}", camera.ZoomValue, "Camera ZoomValue");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.ZoomDuration)}", camera.ZoomDuration, "Camera ZoomDuration");
                    break;
                case CameraAction.SlowMotion:
                    RequireRange(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.TimeScale)}", camera.TimeScale, 0.01f, 1f, "Camera TimeScale");
                    RequirePositive(skillVisual, issues, $"{fieldPath}.{nameof(CameraControlEvent.SlowMotionDuration)}", camera.SlowMotionDuration, "Camera SlowMotionDuration");
                    break;
            }
        }

        private static void RequireId(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} is empty."));
            }
            else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace."));
            }
        }

        private static void RequireDisplayName(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Warning, fieldPath, $"{label} is empty."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value <= 0f)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value < 0)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNormalized(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value > 1f)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must be between 0 and 1."));
            }
        }

        private static void RequireRange(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            float value,
            float minInclusive,
            float maxInclusive,
            string label)
        {
            if (value < minInclusive || value > maxInclusive)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must be between {minInclusive:0.###} and {maxInclusive:0.###}."));
            }
        }

        private static void RequireAtLeast(
            ScriptableObject asset,
            ICollection<RpgValidationIssue> issues,
            string fieldPath,
            float value,
            float minInclusive,
            string label)
        {
            if (value < minInclusive)
            {
                issues.Add(new RpgValidationIssue(asset, RpgValidationSeverity.Error, fieldPath, $"{label} must be greater than or equal to {minInclusive:0.###}."));
            }
        }

        private static void AddDuplicateStringIssues<T>(
            IEnumerable<T> assets,
            Func<T, string> keySelector,
            string fieldPath,
            string label,
            ICollection<RpgValidationIssue> issues)
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
                    issues.Add(new RpgValidationIssue(
                        duplicate.Asset,
                        RpgValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicate.Key}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }
    }
}
