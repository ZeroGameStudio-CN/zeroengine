using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMapDefinition
    {
        public string MapId;
        public string RaidRoomName;
        public int DurationSeconds;
        public int ThreatLevel;
        public bool AllowEmergencyExtraction;
        public bool UnlockedByDefault;
        public string DisplayName;
        public string Description;
        public int DangerLevel;
        public int RecommendedGearLevel;
        public string RaidRuleProfileId;
        public List<string> LootTableIds = new();
        public List<string> SpawnPointIds = new();
        public List<string> CorpseRegionIds = new();

        // C2 内容奖励档。两者都留空时继续使用旧 lootTableIds；新内容地图必须同时填写，
        // 且 LootProfile.ContentTierId 必须与 ContentTierId 一致。
        public string ContentTierId;
        public string LootProfileId;

        // M1 SC.1 新增（additive）：进 raid 时自动发放的起始武器，值须等于一个可解析的
        // WeaponDataSO.ID；留空/未设置 = 本地图不发放起始武器（老配置零改动兼容）。
        public string StarterWeaponDefinitionId;

        // M1 SB.1 新增（additive）：loot 布点 id 列表，和 SpawnPointIds(敌人布点)是独立概念——
        // 同一个 Transform 理论上可以两者共用同一个 id 字符串，但场景标记组件类型不同
        // (ExtractionSpawnPointActor vs ExtractionLootSpawnPointActor)，互不误配。
        public List<string> LootSpawnPointIds = new();

        // M2 SS.1 新增（additive）：上涌危险参数。HazardEnabled=false(默认)=本地图不启用该机制，
        // 老配置零改动兼容。HazardRiseRate 单位=每秒上升高度；HazardDamagePerSecond 按每次 tick
        // (1 秒一次)整量伤害，不是每帧分摊。
        public bool HazardEnabled;
        public float HazardStartY;
        public float HazardRiseRate;
        public float HazardDamagePerSecond;

        // M2 SM.1 新增（additive）：解锁本地图所需的物品成本列表。留空/未设置(默认)=本地图
        // 解锁免费，和老配置零改动兼容，走既有 ExtractionMapAccessService.TryUnlockMap 的免费路径；
        // 非空则须走 ExtractionMapUnlockTransactionService.TryUnlockMapWithCost 消耗后解锁。
        public List<ExtractionItemCost> UnlockCosts = new();

        // M2 SM.4 新增（additive）：列在此处的 spawnPointId 固定只从本地图 LootTableIds 里
        // IsRare 的子集中选表(见 ExtractionRoomLootBridge.ResolveLootTableIdsForSpawnPoint)，
        // 而不是走正常的"按 hash 覆盖全表"选择。留空/未设置(默认)=该机制不生效，和老配置零改动兼容。
        public List<string> RareLootSpawnPointIds = new();

        public bool IsValid =>
            !string.IsNullOrEmpty(MapId)
            && !string.IsNullOrEmpty(RaidRoomName)
            && DurationSeconds > 0;

        public ExtractionMapDefinition(
            string mapId,
            string raidRoomName,
            int durationSeconds,
            int threatLevel,
            bool allowEmergencyExtraction,
            string displayName = "",
            string description = "",
            int dangerLevel = 0,
            int recommendedGearLevel = 0,
            IEnumerable<string> lootTableIds = null,
            IEnumerable<string> spawnPointIds = null,
            IEnumerable<string> corpseRegionIds = null,
            bool unlockedByDefault = false)
        {
            MapId = mapId;
            RaidRoomName = raidRoomName;
            DurationSeconds = durationSeconds;
            ThreatLevel = threatLevel;
            AllowEmergencyExtraction = allowEmergencyExtraction;
            UnlockedByDefault = unlockedByDefault;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            DangerLevel = dangerLevel;
            RecommendedGearLevel = recommendedGearLevel;
            if (lootTableIds != null)
                LootTableIds.AddRange(lootTableIds);
            if (spawnPointIds != null)
                SpawnPointIds.AddRange(spawnPointIds);
            if (corpseRegionIds != null)
                CorpseRegionIds.AddRange(corpseRegionIds);
        }
    }
}
