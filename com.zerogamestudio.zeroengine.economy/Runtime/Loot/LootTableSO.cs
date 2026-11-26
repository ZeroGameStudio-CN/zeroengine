using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Inventory;

namespace ZeroEngine.Loot
{
    /// <summary>
    /// 保底配置
    /// </summary>
    [Serializable]
    public class PityConfig
    {
        [Tooltip("保底次数（N次未出必出）")]
        public int MaxAttempts = 10;

        [Tooltip("每次失败增加的概率 (0-1)")]
        [Range(0f, 1f)]
        public float IncrementPerFail = 0.1f;

        [Tooltip("是否全局共享保底（否则每个玩家独立）")]
        public bool GlobalPity = false;
    }

    /// <summary>
    /// 掉落条目
    /// </summary>
    [Serializable]
    public class LootEntry
    {
        [Tooltip("条目类型")]
        public LootEntryType Type = LootEntryType.Item;

        [Header("物品掉落 (Type=Item)")]
        [Tooltip("物品引用")]
        public InventoryItemSO Item;

        [Header("嵌套表 (Type=Table)")]
        [Tooltip("嵌套掉落表")]
        public LootTableSO NestedTable;

        [Header("货币掉落 (Type=Currency)")]
        [Tooltip("货币类型")]
        public CurrencyType Currency;

        [Header("数量")]
        [Tooltip("最小数量")]
        public int AmountMin = 1;

        [Tooltip("最大数量")]
        public int AmountMax = 1;

        [Header("权重")]
        [Tooltip("权重值（相对于其他条目）")]
        public float Weight = 1f;

        [Header("条件")]
        [Tooltip("条件列表（全部满足才能掉落）")]
        [SerializeReference]
        public List<LootCondition> Conditions = new List<LootCondition>();

        [Header("保底")]
        [Tooltip("保底配置（可选）")]
        public PityConfig Pity;

        /// <summary>
        /// 获取随机数量
        /// </summary>
        public int GetRandomAmount()
        {
            if (AmountMin >= AmountMax)
                return AmountMin;
            return UnityEngine.Random.Range(AmountMin, AmountMax + 1);
        }

        /// <summary>
        /// 检查所有条件
        /// </summary>
        public bool CheckConditions(LootContext context)
        {
            if (Conditions == null || Conditions.Count == 0)
                return true;

            for (int i = 0; i < Conditions.Count; i++)
            {
                if (Conditions[i] != null && !Conditions[i].Check(context))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 获取显示名称
        /// </summary>
        public string GetDisplayName()
        {
            return Type switch
            {
                LootEntryType.Item => Item?.ItemName ?? "(无物品)",
                LootEntryType.Table => NestedTable?.DisplayName ?? "(无掉落表)",
                LootEntryType.Currency => Currency.ToString(),
                LootEntryType.Nothing => "(空)",
                _ => "(未知)"
            };
        }
    }

    /// <summary>
    /// 分层掉落配置（用于 Layered 模式）
    /// </summary>
    [Serializable]
    public class LootLayer
    {
        [Tooltip("层名称（如：品质层）")]
        public string LayerName;

        [Tooltip("层权重")]
        public float Weight = 1f;

        [Tooltip("此层的掉落条目")]
        public List<LootEntry> Entries = new List<LootEntry>();
    }

    /// <summary>
    /// 掉落表 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "New LootTable", menuName = "ZeroEngine/Items and Economy/Loot/Loot Table")]
    public class LootTableSO : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("掉落表ID")]
        public string TableId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("描述")]
        [TextArea(2, 4)]
        public string Description;

        [Header("掉落模式")]
        [Tooltip("掉落模式")]
        public LootDropMode DropMode = LootDropMode.Weight;

        [Header("掉落配置")]
        [Tooltip("每次抽取的掉落数量")]
        public int DropCount = 1;

        [Tooltip("最大掉落数量（0=无限制，用于保底等特殊情况）")]
        public int MaxDropCount = 0;

        [Tooltip("是否允许重复掉落同一条目")]
        public bool AllowDuplicates = true;

        [Header("条目列表 (Weight/Pity 模式)")]
        [Tooltip("掉落条目")]
        public List<LootEntry> Entries = new List<LootEntry>();

        [Header("分层配置 (Layered 模式)")]
        [Tooltip("分层列表")]
        public List<LootLayer> Layers = new List<LootLayer>();

        [Header("必掉物品")]
        [Tooltip("每次必定掉落的物品")]
        public List<LootEntry> GuaranteedDrops = new List<LootEntry>();

        [Header("全局条件")]
        [Tooltip("整个掉落表的条件（不满足则不掉落任何东西）")]
        [SerializeReference]
        public List<LootCondition> GlobalConditions = new List<LootCondition>();

        /// <summary>
        /// 检查全局条件
        /// </summary>
        public bool CheckGlobalConditions(LootContext context)
        {
            if (GlobalConditions == null || GlobalConditions.Count == 0)
                return true;

            for (int i = 0; i < GlobalConditions.Count; i++)
            {
                if (GlobalConditions[i] != null && !GlobalConditions[i].Check(context))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 获取有效条目（满足条件的）
        /// </summary>
        public void GetValidEntries(LootContext context, List<LootEntry> results)
        {
            results.Clear();

            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (entry.Weight > 0 && entry.CheckConditions(context))
                {
                    results.Add(entry);
                }
            }
        }

        /// <summary>
        /// 计算总权重
        /// </summary>
        public float CalculateTotalWeight(List<LootEntry> entries)
        {
            float total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                total += entries[i].Weight;
            }
            return total;
        }

        /// <summary>
        /// 验证掉落表配置
        /// </summary>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(TableId))
            {
                error = "TableId 不能为空";
                return false;
            }

            if (DropMode == LootDropMode.Layered && (Layers == null || Layers.Count == 0))
            {
                error = "Layered 模式需要配置 Layers";
                return false;
            }

            if (DropMode != LootDropMode.Layered && (Entries == null || Entries.Count == 0))
            {
                error = "需要至少一个掉落条目";
                return false;
            }

            // 检查嵌套表循环引用
            if (!CheckNestedTableCycle(this, new HashSet<LootTableSO>()))
            {
                error = "检测到嵌套表循环引用";
                return false;
            }

            error = null;
            return true;
        }

        private bool CheckNestedTableCycle(LootTableSO table, HashSet<LootTableSO> visited)
        {
            if (table == null) return true;
            if (visited.Contains(table)) return false;

            visited.Add(table);

            foreach (var entry in table.Entries)
            {
                if (entry.Type == LootEntryType.Table && entry.NestedTable != null)
                {
                    if (!CheckNestedTableCycle(entry.NestedTable, visited))
                        return false;
                }
            }

            visited.Remove(table);
            return true;
        }

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(TableId))
            {
                TableId = name;
            }
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = name;
            }
        }
    }
}