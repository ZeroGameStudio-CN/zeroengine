// ============================================================================
// ZeroEngine v2.6.0 - Encounter System
// 随机遭遇表配置
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.RPG.TurnBased;

namespace ZeroEngine.RPG.Encounter
{
    /// <summary>
    /// 遭遇条目 - 定义一组敌人的遭遇配置
    /// </summary>
    [Serializable]
    public class EncounterEntry
    {
        [Tooltip("条目 ID (用于保底等逻辑)")]
        public string EntryId;

        [Tooltip("敌人 ID 列表")]
        public List<string> EnemyIds = new List<string>();

        [Tooltip("敌人数量范围 (最小)")]
        [Range(1, 10)]
        public int MinCount = 1;

        [Tooltip("敌人数量范围 (最大)")]
        [Range(1, 10)]
        public int MaxCount = 3;

        [Tooltip("权重 (越高越容易出现)")]
        [Min(0)]
        public float Weight = 1f;

        [Tooltip("最低玩家等级要求")]
        [Min(1)]
        public int MinPlayerLevel = 1;

        [Tooltip("最高玩家等级限制 (0 = 无限制)")]
        [Min(0)]
        public int MaxPlayerLevel = 0;

        [Tooltip("是否为精英遭遇 (更难但奖励更好)")]
        public bool IsElite;

        [Tooltip("是否为 Boss 遭遇 (强制/剧情触发)")]
        public bool IsBoss;

        [Tooltip("战斗模式 (Classic=经典回合制, Tactics=战棋)")]
        public BattleMode BattleMode = BattleMode.Classic;

        /// <summary>
        /// 检查玩家等级是否满足条件
        /// </summary>
        public bool IsValidForLevel(int playerLevel)
        {
            if (playerLevel < MinPlayerLevel) return false;
            if (MaxPlayerLevel > 0 && playerLevel > MaxPlayerLevel) return false;
            return true;
        }

        /// <summary>
        /// 获取随机敌人数量
        /// </summary>
        public int GetRandomCount()
        {
            return UnityEngine.Random.Range(MinCount, MaxCount + 1);
        }
    }

    /// <summary>
    /// 遭遇表 ScriptableObject - 定义区域内可能的遭遇
    /// </summary>
    [CreateAssetMenu(fileName = "EncounterTable", menuName = "ZeroEngine/RPG/Encounter Table")]
    public class EncounterTableSO : ScriptableObject
    {
        [Header("基础配置")]
        [Tooltip("遭遇表 ID")]
        public string TableId;

        [Tooltip("显示名称 (区域名)")]
        public string DisplayName;

        [Tooltip("区域等级范围 (用于敌人缩放)")]
        public Vector2Int LevelRange = new Vector2Int(1, 10);

        [Header("遭遇率配置")]
        [Tooltip("基础遭遇率 (0-1)")]
        [Range(0f, 1f)]
        public float BaseEncounterRate = 0.1f;

        [Tooltip("每步增加的遭遇率")]
        [Range(0f, 0.1f)]
        public float RatePerStep = 0.02f;

        [Tooltip("最大遭遇率")]
        [Range(0f, 1f)]
        public float MaxEncounterRate = 0.5f;

        [Tooltip("遭遇后冷却步数 (保护期)")]
        [Min(0)]
        public int CooldownSteps = 3;

        [Header("遭遇条目")]
        [Tooltip("普通遭遇列表")]
        public List<EncounterEntry> NormalEntries = new List<EncounterEntry>();

        [Tooltip("精英遭遇列表")]
        public List<EncounterEntry> EliteEntries = new List<EncounterEntry>();

        [Tooltip("Boss 遭遇列表")]
        public List<EncounterEntry> BossEntries = new List<EncounterEntry>();

        [Header("精英/Boss 配置")]
        [Tooltip("精英遭遇出现概率")]
        [Range(0f, 1f)]
        public float EliteChance = 0.1f;

        [Tooltip("精英遭遇最低步数")]
        [Min(0)]
        public int EliteMinSteps = 50;

        // ========================================
        // 查询方法
        // ========================================

        // 缓存 List 避免每次调用分配新对象
        [System.NonSerialized] private List<EncounterEntry> _cachedValidEntries;

        /// <summary>
        /// 根据玩家等级获取有效的普通遭遇 (零 GC 版本)
        /// 注意: 返回的 List 为内部缓存，请勿持有引用或修改内容
        /// </summary>
        public List<EncounterEntry> GetValidNormalEntries(int playerLevel)
        {
            _cachedValidEntries ??= new List<EncounterEntry>(16);
            _cachedValidEntries.Clear();

            for (int i = 0; i < NormalEntries.Count; i++)
            {
                var entry = NormalEntries[i];
                if (entry.IsValidForLevel(playerLevel))
                {
                    _cachedValidEntries.Add(entry);
                }
            }
            return _cachedValidEntries;
        }

        /// <summary>
        /// 根据玩家等级获取有效的精英遭遇 (零 GC 版本)
        /// 注意: 返回的 List 为内部缓存，请勿持有引用或修改内容
        /// </summary>
        public List<EncounterEntry> GetValidEliteEntries(int playerLevel)
        {
            _cachedValidEntries ??= new List<EncounterEntry>(16);
            _cachedValidEntries.Clear();

            for (int i = 0; i < EliteEntries.Count; i++)
            {
                var entry = EliteEntries[i];
                if (entry.IsValidForLevel(playerLevel))
                {
                    _cachedValidEntries.Add(entry);
                }
            }
            return _cachedValidEntries;
        }

        /// <summary>
        /// 根据权重随机选择一个遭遇
        /// </summary>
        public EncounterEntry SelectWeightedEntry(List<EncounterEntry> entries)
        {
            if (entries == null || entries.Count == 0) return null;

            float totalWeight = 0f;
            foreach (var entry in entries)
            {
                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0f) return entries[0];

            float random = UnityEngine.Random.Range(0f, totalWeight);
            float current = 0f;

            foreach (var entry in entries)
            {
                current += entry.Weight;
                if (random <= current)
                {
                    return entry;
                }
            }

            return entries[entries.Count - 1];
        }

        /// <summary>
        /// 计算当前遭遇率
        /// </summary>
        public float CalculateEncounterRate(int stepsSinceLastEncounter)
        {
            if (stepsSinceLastEncounter < CooldownSteps) return 0f;

            float rate = BaseEncounterRate + (stepsSinceLastEncounter - CooldownSteps) * RatePerStep;
            return Mathf.Min(rate, MaxEncounterRate);
        }

        /// <summary>
        /// 获取 Boss 遭遇 (通过 ID)
        /// </summary>
        public EncounterEntry GetBossEntry(string entryId)
        {
            foreach (var entry in BossEntries)
            {
                if (entry.EntryId == entryId) return entry;
            }
            return null;
        }

        // ========================================
        // 编辑器工具
        // ========================================

#if UNITY_EDITOR
        [ContextMenu("添加示例普通遭遇")]
        private void AddSampleNormalEntry()
        {
            NormalEntries.Add(new EncounterEntry
            {
                EntryId = $"normal_{NormalEntries.Count + 1}",
                EnemyIds = new List<string> { "slime", "goblin" },
                MinCount = 2,
                MaxCount = 4,
                Weight = 1f
            });
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("添加示例精英遭遇")]
        private void AddSampleEliteEntry()
        {
            EliteEntries.Add(new EncounterEntry
            {
                EntryId = $"elite_{EliteEntries.Count + 1}",
                EnemyIds = new List<string> { "goblin_chief" },
                MinCount = 1,
                MaxCount = 1,
                Weight = 1f,
                IsElite = true
            });
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
