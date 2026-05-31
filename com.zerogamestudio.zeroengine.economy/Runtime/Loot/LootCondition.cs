using System;
using UnityEngine;

namespace ZeroEngine.Loot
{
    /// <summary>
    /// 掉落条件基类（使用 [SerializeReference] 多态）
    /// </summary>
    [Serializable]
    public abstract class LootCondition
    {
        /// <summary>条件描述（用于编辑器显示）</summary>
        public abstract string Description { get; }

        /// <summary>检查条件是否满足</summary>
        public abstract bool Check(LootContext context);
    }

    /// <summary>
    /// 等级条件
    /// </summary>
    [Serializable]
    public class LevelCondition : LootCondition
    {
        [Tooltip("最低等级要求")]
        public int MinLevel = 1;

        [Tooltip("最高等级限制（0=无限制）")]
        public int MaxLevel = 0;

        public override string Description =>
            MaxLevel > 0 ? $"等级 {MinLevel}-{MaxLevel}" : $"等级 >= {MinLevel}";

        public override bool Check(LootContext context)
        {
            if (context == null) return true;

            if (context.PlayerLevel < MinLevel) return false;
            if (MaxLevel > 0 && context.PlayerLevel > MaxLevel) return false;

            return true;
        }
    }

#if ZEROENGINE_NARRATIVE
    /// <summary>
    /// 任务状态条件
    /// </summary>
    [Serializable]
    public class QuestCondition : LootCondition
    {
        [Tooltip("任务ID")]
        public string QuestId;

        [Tooltip("需要的任务状态")]
        public QuestStateRequirement RequiredState = QuestStateRequirement.Completed;

        public override string Description => $"任务 {QuestId} {RequiredState}";

        public override bool Check(LootContext context)
        {
            if (string.IsNullOrEmpty(QuestId)) return true;

            var questManager = Quest.QuestManager.Instance;
            if (questManager == null) return true;

            var state = questManager.GetQuestState(QuestId);
            return RequiredState switch
            {
                QuestStateRequirement.NotStarted => state == Quest.QuestState.Inactive,
                QuestStateRequirement.Active => questManager.HasActiveQuest(QuestId),
                QuestStateRequirement.Completed => state == Quest.QuestState.TheEnd,
                QuestStateRequirement.Any => true,
                _ => true
            };
        }
    }

    public enum QuestStateRequirement
    {
        NotStarted,
        Active,
        Completed,
        Any
    }
#endif

    /// <summary>
    /// 概率条件（随机启用）
    /// </summary>
    [Serializable]
    public class ProbabilityCondition : LootCondition
    {
        [Tooltip("启用概率 (0-1)")]
        [Range(0f, 1f)]
        public float Probability = 1f;

        public override string Description => $"{Probability * 100:F0}% 概率";

        public override bool Check(LootContext context)
        {
            return UnityEngine.Random.value <= Probability;
        }
    }

    /// <summary>
    /// 拥有物品条件
    /// </summary>
    [Serializable]
    public class HasItemCondition : LootCondition
    {
        [Tooltip("物品引用")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("最低数量")]
        public int MinAmount = 1;

        [Tooltip("是否取反（不拥有时通过）")]
        public bool Invert;

        public override string Description =>
            Invert ? $"不拥有 {Item?.ItemName}" : $"拥有 {Item?.ItemName} x{MinAmount}";

        public override bool Check(LootContext context)
        {
            if (Item == null) return true;

            var inventory = context?.InventoryProvider;
            if (inventory == null) return true;

            bool hasItem = inventory.GetItemCount(Item) >= MinAmount;
            return Invert ? !hasItem : hasItem;
        }
    }

    /// <summary>
    /// 首次掉落条件（基于自定义数据）
    /// </summary>
    [Serializable]
    public class FirstDropCondition : LootCondition
    {
        [Tooltip("掉落标记Key")]
        public string DropKey;

        public override string Description => $"首次掉落 ({DropKey})";

        public override bool Check(LootContext context)
        {
            if (string.IsNullOrEmpty(DropKey)) return true;

            // 检查是否已经掉落过（需要外部系统支持）
            return !context.GetCustom<bool>($"dropped_{DropKey}", false);
        }
    }

    /// <summary>
    /// 时间条件（特定时间段）
    /// </summary>
    [Serializable]
    public class TimeCondition : LootCondition
    {
        [Tooltip("开始小时 (0-23)")]
        [Range(0, 23)]
        public int StartHour = 0;

        [Tooltip("结束小时 (0-23)")]
        [Range(0, 23)]
        public int EndHour = 23;

        public override string Description => $"时间 {StartHour}:00-{EndHour}:59";

        public override bool Check(LootContext context)
        {
            int currentHour = DateTime.Now.Hour;

            if (StartHour <= EndHour)
            {
                return currentHour >= StartHour && currentHour <= EndHour;
            }
            else
            {
                // 跨午夜
                return currentHour >= StartHour || currentHour <= EndHour;
            }
        }
    }

    /// <summary>
    /// 组合条件（AND/OR）
    /// </summary>
    [Serializable]
    public class CompositeCondition : LootCondition
    {
        public enum LogicalOperator { And, Or }

        [Tooltip("逻辑运算符")]
        public LogicalOperator Operator = LogicalOperator.And;

        [Tooltip("子条件列表")]
        [SerializeReference]
        public LootCondition[] SubConditions;

        public override string Description =>
            $"({SubConditions?.Length ?? 0} 个条件 {Operator})";

        public override bool Check(LootContext context)
        {
            if (SubConditions == null || SubConditions.Length == 0)
                return true;

            if (Operator == LogicalOperator.And)
            {
                for (int i = 0; i < SubConditions.Length; i++)
                {
                    if (SubConditions[i] != null && !SubConditions[i].Check(context))
                        return false;
                }
                return true;
            }
            else // Or
            {
                for (int i = 0; i < SubConditions.Length; i++)
                {
                    if (SubConditions[i] != null && SubConditions[i].Check(context))
                        return true;
                }
                return false;
            }
        }
    }

    /// <summary>
    /// 自定义条件（回调）
    /// </summary>
    [Serializable]
    public class CustomLootCondition : LootCondition
    {
        [Tooltip("条件ID（用于外部注册）")]
        public string ConditionId;

        public override string Description => $"自定义: {ConditionId}";

        // 静态回调注册表
        private static System.Collections.Generic.Dictionary<string, Func<LootContext, bool>> _callbacks
            = new System.Collections.Generic.Dictionary<string, Func<LootContext, bool>>();

        public static void RegisterCallback(string conditionId, Func<LootContext, bool> callback)
        {
            _callbacks[conditionId] = callback;
        }

        public static void UnregisterCallback(string conditionId)
        {
            _callbacks.Remove(conditionId);
        }

        public override bool Check(LootContext context)
        {
            if (string.IsNullOrEmpty(ConditionId)) return true;

            if (_callbacks.TryGetValue(ConditionId, out var callback))
            {
                return callback(context);
            }

            // 未注册的条件默认通过
            return true;
        }
    }
}
