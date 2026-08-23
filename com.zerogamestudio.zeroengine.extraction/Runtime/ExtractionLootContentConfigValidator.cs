using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public sealed class ExtractionLootContentValidationReport
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();

        public bool IsValid => Errors.Count == 0;
        public string FirstError => Errors.Count > 0 ? Errors[0] : null;

        internal void AddError(string message)
        {
            Errors.Add(message);
        }

        internal void AddWarning(string message)
        {
            Warnings.Add(message);
        }
    }

    public static class ExtractionLootContentConfigValidator
    {
        private static readonly ExtractionItemRarity[] Rarities =
        {
            ExtractionItemRarity.Common,
            ExtractionItemRarity.Uncommon,
            ExtractionItemRarity.Rare,
            ExtractionItemRarity.Epic,
            ExtractionItemRarity.Legendary,
            ExtractionItemRarity.Mythic
        };

        public static ExtractionLootContentValidationReport Validate(ExtractionPlayableConfig config)
        {
            var report = new ExtractionLootContentValidationReport();
            if (config == null)
            {
                report.AddError("搜打撤内容配置为空。");
                return report;
            }

            if (!HasAnyContentConfiguration(config))
            {
                ValidateRaidRules(config, report);
                return report;
            }

            if (!HasRequiredLists(config, report))
                return report;

            var items = CollectItems(config, report);
            var tables = CollectLootTables(config, report);
            var tiers = CollectContentTiers(config, report);
            var profiles = CollectLootProfiles(config, report);
            var regions = CollectRegions(config, report);
            var containers = CollectContainers(config, report);
            var spawns = CollectSpawns(config, report);
            if (!report.IsValid)
                return report;

            ValidateMapReferences(config, tiers, profiles, report);
            ValidateProfileReferences(profiles, tiers, regions, report);
            ValidateRegionReferences(regions, containers, spawns, report);
            ValidateSpawnReferences(spawns, regions, containers, report);
            ValidateContainerTables(containers, tables, items, report);
            if (!report.IsValid)
                return report;

            ValidateProfileCapacityAndAvailability(profiles, regions, spawns, containers, tables, items, report);
            ValidateRaidRules(config, report);
            return report;
        }

        private static void ValidateRaidRules(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            bool hasRules = HasEntries(config.RaidRuleProfiles)
                || HasEntries(config.RaidPhaseRules)
                || HasEntries(config.RaidEffects);
            if (!hasRules)
            {
                foreach (var map in config.Maps)
                {
                    if (map != null && !string.IsNullOrEmpty(map.RaidRuleProfileId))
                    {
                        hasRules = true;
                        break;
                    }
                }
            }
            if (!hasRules) return;

            var profiles = new Dictionary<string, ExtractionRaidRuleProfileDefinition>();
            foreach (var profile in config.RaidRuleProfiles)
            {
                if (profile == null || !profile.IsValid)
                {
                    report.AddError("Raid 规则 Profile 无效。");
                    continue;
                }
                if (!profiles.TryAdd(profile.ProfileId, profile))
                    report.AddError($"Raid 规则 Profile ID 重复：'{profile.ProfileId}'。");
            }

            var effects = new Dictionary<string, ExtractionRaidEffectDefinition>();
            foreach (var effect in config.RaidEffects)
            {
                if (effect == null || !effect.IsValid || !IsRaidEffectShapeValid(effect))
                {
                    report.AddError($"RaidEffect '{effect?.EffectId}' 的类型、目标或数值无效。");
                    continue;
                }
                if (!effects.TryAdd(effect.EffectId, effect))
                    report.AddError($"RaidEffect ID 重复：'{effect.EffectId}'。");
            }

            var ruleIds = new HashSet<string>();
            foreach (var rule in config.RaidPhaseRules)
            {
                if (rule == null || !rule.IsValid)
                {
                    report.AddError("Raid 阶段规则无效。");
                    continue;
                }
                if (!ruleIds.Add(rule.RuleId))
                    report.AddError($"Raid 阶段规则 ID 重复：'{rule.RuleId}'。");
                if (!profiles.TryGetValue(rule.ProfileId, out var profile))
                    report.AddError($"Raid 阶段规则 '{rule.RuleId}' 引用了不存在的 Profile '{rule.ProfileId}'。");
                else if (rule.RemainingSeconds > profile.DurationSeconds)
                    report.AddError($"Raid 阶段规则 '{rule.RuleId}' 的剩余秒数超过 Profile 时长。");
                if (!effects.ContainsKey(rule.EffectId))
                    report.AddError($"Raid 阶段规则 '{rule.RuleId}' 引用了不存在的 Effect '{rule.EffectId}'。");
            }

            foreach (var map in config.Maps)
            {
                if (map == null || !map.IsValid) continue;
                if (!string.IsNullOrEmpty(map.RaidRuleProfileId)
                    && !profiles.ContainsKey(map.RaidRuleProfileId))
                {
                    report.AddError($"地图 '{map.MapId}' 引用了不存在的 raidRuleProfileId '{map.RaidRuleProfileId}'。");
                }
            }

            var encounters = new HashSet<string>();
            foreach (var encounter in config.HostileExplorerEncounters)
            {
                if (encounter == null || !encounter.IsValid) continue;
                encounters.Add(encounter.EncounterId);
            }
            var containerSpawns = new HashSet<string>();
            foreach (var spawn in config.ContainerSpawns)
                if (spawn != null && !string.IsNullOrEmpty(spawn.SpawnId)) containerSpawns.Add(spawn.SpawnId);
            var pointIds = new HashSet<string>();
            foreach (var point in config.ExtractionPoints)
            {
                if (point == null || !point.IsValid)
                    report.AddError($"撤离点 '{point?.PointId}' 的模式字段无效。");
                else pointIds.Add(point.PointId);
            }

            foreach (var effect in effects.Values)
            {
                if (effect.EffectType == ExtractionRaidEffectType.SpawnEncounter
                    && !encounters.Contains(effect.TargetId))
                    report.AddError($"RaidEffect '{effect.EffectId}' 引用了不存在的 Encounter '{effect.TargetId}'。");
                if (effect.EffectType == ExtractionRaidEffectType.SpawnContainer
                    && !containerSpawns.Contains(effect.TargetId))
                    report.AddError($"RaidEffect '{effect.EffectId}' 引用了不存在的 ContainerSpawn '{effect.TargetId}'。");
                if (effect.EffectType == ExtractionRaidEffectType.AdvanceExtractionPointSeconds
                    && !pointIds.Contains(effect.TargetId))
                    report.AddError($"RaidEffect '{effect.EffectId}' 引用了不存在的撤离点 '{effect.TargetId}'。");
            }

            foreach (var gate in config.LeverDefinitions)
            {
                if (gate == null || gate.ChannelSeconds <= 0) continue;
                if (!gate.IsGateConfigurationValid)
                    report.AddError($"Gate '{gate.LeverId}' 的模式字段无效。");
                if (gate.Mode == ExtractionGateMode.Capture
                    && !encounters.Contains(gate.CaptureEncounterId))
                    report.AddError($"Gate '{gate.LeverId}' 引用了不存在的 Capture Encounter '{gate.CaptureEncounterId}'。");
                foreach (var effectId in gate.EffectIds)
                    if (!effects.ContainsKey(effectId))
                        report.AddError($"Gate '{gate.LeverId}' 引用了不存在的 Effect '{effectId}'。");
            }
        }

        private static bool IsRaidEffectShapeValid(ExtractionRaidEffectDefinition effect)
        {
            switch (effect.EffectType)
            {
                case ExtractionRaidEffectType.EnemyStatMultiplier:
                    return !IsBlank(effect.TargetId) && effect.Amount > 0f;
                case ExtractionRaidEffectType.SpawnEncounter:
                case ExtractionRaidEffectType.SpawnContainer:
                case ExtractionRaidEffectType.SetRaidFlag:
                case ExtractionRaidEffectType.UnlockBonusContainerGroup:
                    return !IsBlank(effect.TargetId);
                case ExtractionRaidEffectType.ExtendRaidDeadlineSeconds:
                case ExtractionRaidEffectType.AddThreatLevel:
                    return IsBlank(effect.TargetId) && effect.Amount > 0f;
                case ExtractionRaidEffectType.AdvanceExtractionPointSeconds:
                    return !IsBlank(effect.TargetId) && effect.Amount > 0f;
                default:
                    return false;
            }
        }

        public static bool HasAnyContentConfiguration(ExtractionPlayableConfig config)
        {
            if (config == null) return false;
            if (HasEntries(config.ContentTiers)
                || HasEntries(config.LootProfiles)
                || HasEntries(config.LootRegions)
                || HasEntries(config.ContainerDefinitions)
                || HasEntries(config.ContainerSpawns))
            {
                return true;
            }

            if (config.Maps == null) return false;
            foreach (var map in config.Maps)
            {
                if (map == null) continue;
                if (!string.IsNullOrEmpty(map.ContentTierId) || !string.IsNullOrEmpty(map.LootProfileId))
                    return true;
            }

            return false;
        }

        private static bool HasRequiredLists(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            bool valid = true;
            valid &= RequireNonEmpty(config.ContentTiers, "内容档列表", report);
            valid &= RequireNonEmpty(config.LootProfiles, "掉落配置列表", report);
            valid &= RequireNonEmpty(config.LootRegions, "掉落区域列表", report);
            valid &= RequireNonEmpty(config.ContainerDefinitions, "容器定义列表", report);
            valid &= RequireNonEmpty(config.ContainerSpawns, "容器生成点列表", report);
            return valid;
        }

        private static Dictionary<string, ExtractionItemDefinition> CollectItems(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionItemDefinition>();
            if (config.ItemDefinitions == null)
            {
                report.AddError("物品定义列表为空。");
                return result;
            }

            foreach (var item in config.ItemDefinitions)
            {
                if (item == null || IsBlank(item.DefinitionId) || !Enum.IsDefined(typeof(ExtractionItemRarity), item.Rarity))
                {
                    report.AddError("内容配置包含无效物品定义或品质。");
                    continue;
                }

                if (result.ContainsKey(item.DefinitionId))
                    report.AddError($"物品定义 ID '{item.DefinitionId}' 重复。");
                else
                    result.Add(item.DefinitionId, item);
            }

            return result;
        }

        private static Dictionary<string, ExtractionLootTableDefinition> CollectLootTables(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionLootTableDefinition>();
            if (config.LootTables == null)
            {
                report.AddError("掉落表列表为空。");
                return result;
            }

            foreach (var table in config.LootTables)
            {
                if (table == null || IsBlank(table.TableId) || table.Entries == null || table.Entries.Count == 0)
                {
                    report.AddError("内容配置包含无效掉落表。");
                    continue;
                }

                if (result.ContainsKey(table.TableId))
                    report.AddError($"掉落表 ID '{table.TableId}' 重复。");
                else
                    result.Add(table.TableId, table);
            }

            return result;
        }

        private static Dictionary<string, ExtractionContentTierDefinition> CollectContentTiers(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionContentTierDefinition>();
            foreach (var tier in config.ContentTiers)
            {
                if (tier == null || IsBlank(tier.ContentTierId))
                {
                    report.AddError("内容档定义为空或 ID 无效。");
                    continue;
                }

                if (!ValidatePositiveCurve(tier.RarityWeightMultipliers))
                    report.AddError($"内容档 '{tier.ContentTierId}' 的六档品质倍率必须全部大于 0。");
                else if (IsUniform(tier.RarityWeightMultipliers))
                    report.AddWarning($"内容档 '{tier.ContentTierId}' 的六档品质曲线是统一倍率，不会改变归一化后的掉落概率。");

                if (result.ContainsKey(tier.ContentTierId))
                    report.AddError($"内容档 ID '{tier.ContentTierId}' 重复。");
                else
                    result.Add(tier.ContentTierId, tier);
            }

            return result;
        }

        private static Dictionary<string, ExtractionLootProfileDefinition> CollectLootProfiles(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionLootProfileDefinition>();
            foreach (var profile in config.LootProfiles)
            {
                if (profile == null || IsBlank(profile.LootProfileId) || IsBlank(profile.ContentTierId))
                {
                    report.AddError("掉落配置为空，或 LootProfileId/ContentTierId 无效。");
                    continue;
                }

                if (!ValidateMinimumDrops(profile.MinimumGeneratedDropsByRarity))
                    report.AddError($"掉落配置 '{profile.LootProfileId}' 的六档最低生成数必须全部大于等于 0。");
                if (!ValidatePity(profile.Pity))
                    report.AddError($"掉落配置 '{profile.LootProfileId}' 的珍品保底参数无效。");
                if (!ValidateRevealTimes(profile.RevealTimes))
                    report.AddError($"掉落配置 '{profile.LootProfileId}' 的六档显现时间必须全部大于 0 且严格递增。");
                if (profile.RegionIds == null || profile.RegionIds.Count == 0 || HasBlankOrDuplicate(profile.RegionIds))
                    report.AddError($"掉落配置 '{profile.LootProfileId}' 必须引用至少一个且不重复的区域 ID。");

                if (result.ContainsKey(profile.LootProfileId))
                    report.AddError($"掉落配置 ID '{profile.LootProfileId}' 重复。");
                else
                    result.Add(profile.LootProfileId, profile);
            }

            return result;
        }

        private static Dictionary<string, ExtractionLootRegionDefinition> CollectRegions(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionLootRegionDefinition>();
            foreach (var region in config.LootRegions)
            {
                if (region == null || IsBlank(region.RegionId))
                {
                    report.AddError("掉落区域定义为空或区域 ID 无效。");
                    continue;
                }

                if (!ValidatePositiveCurve(region.RarityWeightMultipliers))
                    report.AddError($"区域 '{region.RegionId}' 的六档品质倍率必须全部大于 0。");
                else if (IsUniform(region.RarityWeightMultipliers))
                    report.AddWarning($"区域 '{region.RegionId}' 的六档品质曲线是统一倍率，不会改变归一化后的掉落概率。");
                if (region.AllowedContainerTypeIds == null
                    || region.AllowedContainerTypeIds.Count == 0
                    || HasBlankOrDuplicate(region.AllowedContainerTypeIds))
                {
                    report.AddError($"区域 '{region.RegionId}' 必须配置至少一个且不重复的允许容器类型。");
                }
                if (region.ContainerSpawnIds == null
                    || region.ContainerSpawnIds.Count == 0
                    || HasBlankOrDuplicate(region.ContainerSpawnIds))
                {
                    report.AddError($"区域 '{region.RegionId}' 必须配置至少一个且不重复的容器生成点。");
                }

                if (result.ContainsKey(region.RegionId))
                    report.AddError($"区域 ID '{region.RegionId}' 重复。");
                else
                    result.Add(region.RegionId, region);
            }

            return result;
        }

        private static Dictionary<string, ExtractionContainerDefinition> CollectContainers(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionContainerDefinition>();
            foreach (var container in config.ContainerDefinitions)
            {
                if (container == null || IsBlank(container.ContainerTypeId))
                {
                    report.AddError("容器定义为空或容器类型 ID 无效。");
                    continue;
                }

                if (container.Capacity <= 0
                    || container.MinimumContentCount <= 0
                    || container.MaximumContentCount < container.MinimumContentCount
                    || container.MaximumContentCount > container.Capacity)
                {
                    report.AddError($"容器 '{container.ContainerTypeId}' 的内容数量区间必须为正、min<=max，且 max 不得超过容量。");
                }
                if (container.SearchTimeMultiplier <= 0f)
                    report.AddError($"容器 '{container.ContainerTypeId}' 的搜索时间倍率必须大于 0。");
                if (container.LootTableIds == null
                    || container.LootTableIds.Count == 0
                    || HasBlankOrDuplicate(container.LootTableIds))
                {
                    report.AddError($"容器 '{container.ContainerTypeId}' 必须引用至少一个且不重复的掉落表。");
                }

                if (result.ContainsKey(container.ContainerTypeId))
                    report.AddError($"容器类型 ID '{container.ContainerTypeId}' 重复。");
                else
                    result.Add(container.ContainerTypeId, container);
            }

            return result;
        }

        private static Dictionary<string, ExtractionContainerSpawnDefinition> CollectSpawns(
            ExtractionPlayableConfig config,
            ExtractionLootContentValidationReport report)
        {
            var result = new Dictionary<string, ExtractionContainerSpawnDefinition>();
            foreach (var spawn in config.ContainerSpawns)
            {
                if (spawn == null || IsBlank(spawn.SpawnId) || IsBlank(spawn.RegionId))
                {
                    report.AddError("容器生成点为空，或 SpawnId/RegionId 无效。");
                    continue;
                }

                if (spawn.Always == spawn.ChancePerRaid)
                    report.AddError($"容器生成点 '{spawn.SpawnId}' 必须且只能启用 Always 与 ChancePerRaid 其中一种模式。");
                else if (spawn.ChancePerRaid && (spawn.Chance <= 0f || spawn.Chance > 1f))
                    report.AddError($"容器生成点 '{spawn.SpawnId}' 的 ChancePerRaid 出现率必须在 (0, 1] 内。");
                else if (spawn.Always && Math.Abs(spawn.Chance - 1f) > 0.0001f)
                    report.AddError($"固定容器生成点 '{spawn.SpawnId}' 的出现率必须为 1。");

                if (spawn.Candidates == null || spawn.Candidates.Count == 0)
                {
                    report.AddError($"容器生成点 '{spawn.SpawnId}' 至少需要一个候选容器类型。");
                }
                else
                {
                    var candidateIds = new HashSet<string>();
                    foreach (var candidate in spawn.Candidates)
                    {
                        if (candidate == null || IsBlank(candidate.ContainerTypeId) || candidate.Weight <= 0)
                        {
                            report.AddError($"容器生成点 '{spawn.SpawnId}' 包含无效候选或非正权重。");
                            continue;
                        }
                        if (!candidateIds.Add(candidate.ContainerTypeId))
                            report.AddError($"容器生成点 '{spawn.SpawnId}' 重复引用候选容器 '{candidate.ContainerTypeId}'。");
                    }
                }

                if (result.ContainsKey(spawn.SpawnId))
                    report.AddError($"容器生成点 ID '{spawn.SpawnId}' 重复。");
                else
                    result.Add(spawn.SpawnId, spawn);
            }

            return result;
        }

        private static void ValidateMapReferences(
            ExtractionPlayableConfig config,
            Dictionary<string, ExtractionContentTierDefinition> tiers,
            Dictionary<string, ExtractionLootProfileDefinition> profiles,
            ExtractionLootContentValidationReport report)
        {
            if (config.Maps == null) return;
            foreach (var map in config.Maps)
            {
                if (map == null) continue;
                bool hasTier = !IsBlank(map.ContentTierId);
                bool hasProfile = !IsBlank(map.LootProfileId);
                if (!hasTier && !hasProfile) continue;
                if (!hasTier || !hasProfile)
                {
                    report.AddError($"地图 '{map.MapId}' 必须同时配置 ContentTierId 与 LootProfileId。");
                    continue;
                }
                if (!tiers.ContainsKey(map.ContentTierId))
                    report.AddError($"地图 '{map.MapId}' 引用了不存在的内容档 '{map.ContentTierId}'。");
                if (!profiles.TryGetValue(map.LootProfileId, out var profile))
                    report.AddError($"地图 '{map.MapId}' 引用了不存在的掉落配置 '{map.LootProfileId}'。");
                else if (profile.ContentTierId != map.ContentTierId)
                    report.AddError($"地图 '{map.MapId}' 的内容档与掉落配置所属内容档不一致。");
            }
        }

        private static void ValidateProfileReferences(
            Dictionary<string, ExtractionLootProfileDefinition> profiles,
            Dictionary<string, ExtractionContentTierDefinition> tiers,
            Dictionary<string, ExtractionLootRegionDefinition> regions,
            ExtractionLootContentValidationReport report)
        {
            foreach (var pair in profiles)
            {
                var profile = pair.Value;
                if (!tiers.ContainsKey(profile.ContentTierId))
                    report.AddError($"掉落配置 '{profile.LootProfileId}' 引用了不存在的内容档 '{profile.ContentTierId}'。");
                if (profile.RegionIds == null) continue;
                foreach (var regionId in profile.RegionIds)
                {
                    if (!regions.ContainsKey(regionId))
                        report.AddError($"掉落配置 '{profile.LootProfileId}' 引用了不存在的区域 '{regionId}'。");
                }
            }
        }

        private static void ValidateRegionReferences(
            Dictionary<string, ExtractionLootRegionDefinition> regions,
            Dictionary<string, ExtractionContainerDefinition> containers,
            Dictionary<string, ExtractionContainerSpawnDefinition> spawns,
            ExtractionLootContentValidationReport report)
        {
            foreach (var pair in regions)
            {
                var region = pair.Value;
                if (region.AllowedContainerTypeIds != null)
                {
                    foreach (var containerId in region.AllowedContainerTypeIds)
                    {
                        if (!containers.ContainsKey(containerId))
                            report.AddError($"区域 '{region.RegionId}' 引用了不存在的容器类型 '{containerId}'。");
                    }
                }
                if (region.ContainerSpawnIds == null) continue;
                foreach (var spawnId in region.ContainerSpawnIds)
                {
                    if (!spawns.TryGetValue(spawnId, out var spawn))
                        report.AddError($"区域 '{region.RegionId}' 引用了不存在的容器生成点 '{spawnId}'。");
                    else if (spawn.RegionId != region.RegionId)
                        report.AddError($"容器生成点 '{spawnId}' 的 RegionId 与区域 '{region.RegionId}' 不一致。");
                }
            }
        }

        private static void ValidateSpawnReferences(
            Dictionary<string, ExtractionContainerSpawnDefinition> spawns,
            Dictionary<string, ExtractionLootRegionDefinition> regions,
            Dictionary<string, ExtractionContainerDefinition> containers,
            ExtractionLootContentValidationReport report)
        {
            foreach (var pair in spawns)
            {
                var spawn = pair.Value;
                if (!regions.TryGetValue(spawn.RegionId, out var region))
                {
                    report.AddError($"容器生成点 '{spawn.SpawnId}' 引用了不存在的区域 '{spawn.RegionId}'。");
                    continue;
                }
                if (region.ContainerSpawnIds == null || !region.ContainerSpawnIds.Contains(spawn.SpawnId))
                    report.AddError($"容器生成点 '{spawn.SpawnId}' 未列入所属区域 '{spawn.RegionId}' 的生成点列表。");
                if (spawn.Candidates == null) continue;
                foreach (var candidate in spawn.Candidates)
                {
                    if (candidate == null) continue;
                    if (!containers.ContainsKey(candidate.ContainerTypeId))
                        report.AddError($"容器生成点 '{spawn.SpawnId}' 引用了不存在的容器类型 '{candidate.ContainerTypeId}'。");
                    else if (region.AllowedContainerTypeIds == null
                             || !region.AllowedContainerTypeIds.Contains(candidate.ContainerTypeId))
                    {
                        report.AddError($"容器生成点 '{spawn.SpawnId}' 的候选容器 '{candidate.ContainerTypeId}' 不在区域允许集合内。");
                    }
                }
            }
        }

        private static void ValidateContainerTables(
            Dictionary<string, ExtractionContainerDefinition> containers,
            Dictionary<string, ExtractionLootTableDefinition> tables,
            Dictionary<string, ExtractionItemDefinition> items,
            ExtractionLootContentValidationReport report)
        {
            foreach (var pair in containers)
            {
                var container = pair.Value;
                bool hasValidItem = false;
                if (container.LootTableIds == null) continue;
                foreach (var tableId in container.LootTableIds)
                {
                    if (!tables.TryGetValue(tableId, out var table))
                    {
                        report.AddError($"容器 '{container.ContainerTypeId}' 引用了不存在的掉落表 '{tableId}'。");
                        continue;
                    }
                    hasValidItem |= HasAnyValidItem(table, items, null, false);
                }

                if (!hasValidItem)
                    report.AddError($"容器 '{container.ContainerTypeId}' 的掉落表没有合法物品候选。");
            }
        }

        private static void ValidateProfileCapacityAndAvailability(
            Dictionary<string, ExtractionLootProfileDefinition> profiles,
            Dictionary<string, ExtractionLootRegionDefinition> regions,
            Dictionary<string, ExtractionContainerSpawnDefinition> spawns,
            Dictionary<string, ExtractionContainerDefinition> containers,
            Dictionary<string, ExtractionLootTableDefinition> tables,
            Dictionary<string, ExtractionItemDefinition> items,
            ExtractionLootContentValidationReport report)
        {
            foreach (var pair in profiles)
            {
                var profile = pair.Value;
                var allContainers = new HashSet<string>();
                var guaranteedSpawns = new List<ExtractionContainerSpawnDefinition>();
                CollectProfileContainers(profile, regions, spawns, allContainers, guaranteedSpawns);

                foreach (var containerId in allContainers)
                {
                    if (!containers.TryGetValue(containerId, out var container)) continue;
                    if (!HasReachableItem(container, tables, items, null, true))
                    {
                        report.AddError(
                            $"容器 '{containerId}' 在关闭珍品后没有非珍品候选；禁止回退到珍品表。");
                        return;
                    }
                }

                foreach (var rarity in Rarities)
                {
                    int minimum = profile.MinimumGeneratedDropsByRarity.Get(rarity);
                    if (minimum <= 0) continue;
                    int rarityCapacity = GetMinimumGuaranteedCapacityForRarity(
                        guaranteedSpawns,
                        containers,
                        tables,
                        items,
                        rarity);
                    if (minimum > rarityCapacity)
                    {
                        report.AddError(
                            $"掉落配置 '{profile.LootProfileId}' 为 {rarity} 配置了最低生成数 {minimum}，" +
                            $"但固定容器在所有候选结果下只能承载 {rarityCapacity} 个该品质合法物品。");
                        return;
                    }
                }

                int required = GetTotalMinimumDrops(profile.MinimumGeneratedDropsByRarity);
                int guaranteedCapacity = GetMinimumGuaranteedCapacity(guaranteedSpawns, containers);
                if (required > guaranteedCapacity)
                {
                    report.AddError(
                        $"掉落配置 '{profile.LootProfileId}' 的最低生成数合计 {required} 超过固定容器容量 {guaranteedCapacity}。");
                    return;
                }
            }
        }

        private static void CollectProfileContainers(
            ExtractionLootProfileDefinition profile,
            Dictionary<string, ExtractionLootRegionDefinition> regions,
            Dictionary<string, ExtractionContainerSpawnDefinition> spawns,
            HashSet<string> allContainers,
            List<ExtractionContainerSpawnDefinition> guaranteedSpawns)
        {
            if (profile.RegionIds == null) return;
            foreach (var regionId in profile.RegionIds)
            {
                if (!regions.TryGetValue(regionId, out var region) || region.ContainerSpawnIds == null) continue;
                foreach (var spawnId in region.ContainerSpawnIds)
                {
                    if (!spawns.TryGetValue(spawnId, out var spawn) || spawn.Candidates == null) continue;
                    if (spawn.Always) guaranteedSpawns.Add(spawn);
                    foreach (var candidate in spawn.Candidates)
                    {
                        if (candidate != null) allContainers.Add(candidate.ContainerTypeId);
                    }
                }
            }
        }

        private static int GetMinimumGuaranteedCapacityForRarity(
            List<ExtractionContainerSpawnDefinition> guaranteedSpawns,
            Dictionary<string, ExtractionContainerDefinition> containers,
            Dictionary<string, ExtractionLootTableDefinition> tables,
            Dictionary<string, ExtractionItemDefinition> items,
            ExtractionItemRarity rarity)
        {
            int total = 0;
            foreach (var spawn in guaranteedSpawns)
            {
                int minimum = int.MaxValue;
                foreach (var candidate in spawn.Candidates)
                {
                    if (candidate == null || !containers.TryGetValue(candidate.ContainerTypeId, out var container))
                    {
                        minimum = 0;
                        break;
                    }
                    if (!HasReachableItem(container, tables, items, rarity, false))
                    {
                        minimum = 0;
                        break;
                    }
                    minimum = Math.Min(minimum, container.MaximumContentCount);
                }
                if (minimum != int.MaxValue) total += minimum;
            }

            return total;
        }

        private static int GetMinimumGuaranteedCapacity(
            List<ExtractionContainerSpawnDefinition> guaranteedSpawns,
            Dictionary<string, ExtractionContainerDefinition> containers)
        {
            int total = 0;
            foreach (var spawn in guaranteedSpawns)
            {
                int minimum = int.MaxValue;
                foreach (var candidate in spawn.Candidates)
                {
                    if (candidate == null || !containers.TryGetValue(candidate.ContainerTypeId, out var container))
                        continue;
                    minimum = Math.Min(minimum, container.MaximumContentCount);
                }
                if (minimum != int.MaxValue) total += minimum;
            }
            return total;
        }

        private static bool HasReachableItem(
            ExtractionContainerDefinition container,
            Dictionary<string, ExtractionLootTableDefinition> tables,
            Dictionary<string, ExtractionItemDefinition> items,
            ExtractionItemRarity? requiredRarity,
            bool nonRareOnly)
        {
            if (container.LootTableIds == null) return false;
            foreach (var tableId in container.LootTableIds)
            {
                if (!tables.TryGetValue(tableId, out var table)) continue;
                if (nonRareOnly && table.IsRare) continue;
                if (HasAnyValidItem(table, items, requiredRarity, nonRareOnly)) return true;
            }
            return false;
        }

        private static bool HasAnyValidItem(
            ExtractionLootTableDefinition table,
            Dictionary<string, ExtractionItemDefinition> items,
            ExtractionItemRarity? requiredRarity,
            bool nonRareOnly)
        {
            if (table == null || table.Entries == null) return false;
            foreach (var entry in table.Entries)
            {
                if (entry == null || entry.Weight <= 0 || entry.Quantity <= 0) continue;
                if (!items.TryGetValue(entry.DefinitionId, out var item)) continue;
                if (requiredRarity.HasValue && item.Rarity != requiredRarity.Value) continue;
                if (nonRareOnly && (int)item.Rarity >= (int)ExtractionItemRarity.Rare) continue;
                return true;
            }
            return false;
        }

        private static bool ValidateMinimumDrops(ExtractionRarityIntValues values)
        {
            if (values == null) return false;
            foreach (var rarity in Rarities)
            {
                if (values.Get(rarity) < 0) return false;
            }
            return true;
        }

        private static bool ValidatePity(ExtractionLootPityDefinition pity)
        {
            return pity != null
                   && Enum.IsDefined(typeof(ExtractionItemRarity), pity.TargetRarity)
                   && (int)pity.TargetRarity >= (int)ExtractionItemRarity.Rare
                   && pity.WeightMultiplierIncrementPerMiss > 0f
                   && pity.MaximumWeightMultiplier > 1f;
        }

        private static bool ValidateRevealTimes(ExtractionRevealTimeDefinition revealTimes)
        {
            var values = revealTimes?.BaseRevealSeconds;
            if (!ValidatePositiveCurve(values)) return false;
            float previous = 0f;
            foreach (var rarity in Rarities)
            {
                float current = values.Get(rarity);
                if (current <= previous) return false;
                previous = current;
            }
            return true;
        }

        private static bool ValidatePositiveCurve(ExtractionRarityFloatValues values)
        {
            if (values == null) return false;
            foreach (var rarity in Rarities)
            {
                if (values.Get(rarity) <= 0f) return false;
            }
            return true;
        }

        private static bool IsUniform(ExtractionRarityFloatValues values)
        {
            float common = values.Common;
            foreach (var rarity in Rarities)
            {
                if (Math.Abs(values.Get(rarity) - common) > 0.0001f) return false;
            }
            return true;
        }

        private static int GetTotalMinimumDrops(ExtractionRarityIntValues values)
        {
            int total = 0;
            foreach (var rarity in Rarities)
                total += values.Get(rarity);
            return total;
        }

        private static bool HasBlankOrDuplicate(List<string> values)
        {
            var ids = new HashSet<string>();
            foreach (var value in values)
            {
                if (IsBlank(value) || !ids.Add(value)) return true;
            }
            return false;
        }

        private static bool RequireNonEmpty<T>(
            List<T> values,
            string label,
            ExtractionLootContentValidationReport report)
        {
            if (values != null && values.Count > 0) return true;
            report.AddError($"{label}为空；新内容配置必须完整声明 ContentTier、LootProfile、Region、ContainerDefinition 与 ContainerSpawn。");
            return false;
        }

        private static bool HasEntries<T>(List<T> values)
        {
            return values != null && values.Count > 0;
        }

        private static bool IsBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value);
        }
    }
}
