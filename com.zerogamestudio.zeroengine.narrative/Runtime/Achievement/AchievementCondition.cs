using System;
using UnityEngine;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就条件基类
    /// </summary>
    [Serializable]
    public abstract class AchievementCondition
    {
        /// <summary>条件描述</summary>
        public abstract string Description { get; }

        /// <summary>是否已完成</summary>
        public abstract bool IsCompleted(AchievementProgress progress, int conditionIndex);

        /// <summary>获取进度 (0-1)</summary>
        public abstract float GetProgress(AchievementProgress progress, int conditionIndex);

        /// <summary>获取进度文本</summary>
        public abstract string GetProgressText(AchievementProgress progress, int conditionIndex);

        /// <summary>处理事件</summary>
        public virtual void ProcessEvent(string eventId, object data, AchievementProgress progress, int conditionIndex) { }
    }

    /// <summary>
    /// 计数条件（累计类）
    /// </summary>
    [Serializable]
    public class CounterCondition : AchievementCondition
    {
        [Tooltip("监听的事件ID")]
        public string EventId;

        [Tooltip("目标数量")]
        public int TargetCount = 1;

        [Tooltip("条件描述模板（{0}=当前, {1}=目标）")]
        public string DescriptionTemplate = "完成 {1} 次";

        public override string Description =>
            string.Format(DescriptionTemplate, 0, TargetCount);

        public override bool IsCompleted(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentCount(progress, conditionIndex);
            return current >= TargetCount;
        }

        public override float GetProgress(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentCount(progress, conditionIndex);
            return Mathf.Clamp01((float)current / TargetCount);
        }

        public override string GetProgressText(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentCount(progress, conditionIndex);
            return $"{current}/{TargetCount}";
        }

        public override void ProcessEvent(string eventId, object data, AchievementProgress progress, int conditionIndex)
        {
            if (eventId != EventId) return;

            int increment = 1;
            if (data is int intData)
            {
                increment = intData;
            }

            int current = GetCurrentCount(progress, conditionIndex);
            progress.ConditionProgress[conditionIndex] = Mathf.Min(current + increment, TargetCount);
        }

        private int GetCurrentCount(AchievementProgress progress, int conditionIndex)
        {
            return progress.ConditionProgress.TryGetValue(conditionIndex, out var count) ? count : 0;
        }
    }

    /// <summary>
    /// 状态条件
    /// </summary>
    [Serializable]
    public class StateCondition : AchievementCondition
    {
        [Tooltip("检查类型")]
        public StateCheckType CheckType = StateCheckType.PlayerLevel;

        [Tooltip("目标值")]
        public int TargetValue = 1;

        [Tooltip("物品引用（CheckType=HasItem时使用）")]
        public Inventory.InventoryItemSO Item;

        [Tooltip("货币类型（CheckType=CurrencyAmount时使用）")]
        public Loot.CurrencyType CurrencyType;

        [Tooltip("属性 ID（CheckType=StatValue时使用）")]
        public StatSystem.StatId StatId;

        [Tooltip("自定义检查ID（CheckType=Custom时使用）")]
        public string CustomCheckId;

        public override string Description => CheckType switch
        {
            StateCheckType.PlayerLevel => $"达到等级 {TargetValue}",
            StateCheckType.HasItem => $"拥有 {Item?.ItemName} x{TargetValue}",
            StateCheckType.CurrencyAmount => $"拥有 {CurrencyType} x{TargetValue}",
            StateCheckType.StatValue => $"{StatId} >= {TargetValue}",
            StateCheckType.Custom => $"自定义: {CustomCheckId}",
            _ => "状态条件"
        };

        public override bool IsCompleted(AchievementProgress progress, int conditionIndex)
        {
            return GetCurrentValue() >= TargetValue;
        }

        public override float GetProgress(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentValue();
            return Mathf.Clamp01((float)current / TargetValue);
        }

        public override string GetProgressText(AchievementProgress progress, int conditionIndex)
        {
            int current = GetCurrentValue();
            return $"{current}/{TargetValue}";
        }

        private int GetCurrentValue()
        {
            return CheckType switch
            {
                StateCheckType.PlayerLevel => GetPlayerLevel(),
                StateCheckType.HasItem => GetItemCount(),
                StateCheckType.CurrencyAmount => GetCurrencyAmount(),
                StateCheckType.StatValue => GetStatValue(),
                StateCheckType.Custom => GetCustomValue(),
                _ => 0
            };
        }

        private int GetPlayerLevel()
        {
            // 需要外部系统支持
            return 1;
        }

        private int GetItemCount()
        {
            if (Item == null) return 0;
            var inventory = Inventory.InventoryManager.Instance;
            return inventory?.GetItemCount(Item) ?? 0;
        }

        private int GetCurrencyAmount()
        {
            // 需要外部货币系统支持
            return 0;
        }

        private int GetStatValue()
        {
            // 需要外部属性系统支持
            return 0;
        }

        private int GetCustomValue()
        {
            if (string.IsNullOrEmpty(CustomCheckId)) return 0;

            if (_customChecks.TryGetValue(CustomCheckId, out var check))
            {
                return check();
            }
            return 0;
        }

        // 自定义检查注册
        private static readonly System.Collections.Generic.Dictionary<string, Func<int>> _customChecks =
            new System.Collections.Generic.Dictionary<string, Func<int>>();

        public static void RegisterCustomCheck(string id, Func<int> check)
        {
            _customChecks[id] = check;
        }

        public static void UnregisterCustomCheck(string id)
        {
            _customChecks.Remove(id);
        }
    }

    /// <summary>
    /// 事件条件（一次性触发）
    /// </summary>
    [Serializable]
    public class EventCondition : AchievementCondition
    {
        [Tooltip("监听的事件ID")]
        public string EventId;

        [Tooltip("条件描述")]
        public string ConditionDescription = "触发事件";

        [Tooltip("是否需要特定数据")]
        public bool RequireData;

        [Tooltip("需要的数据值（RequireData=true时使用）")]
        public string RequiredDataValue;

        public override string Description => ConditionDescription;

        public override bool IsCompleted(AchievementProgress progress, int conditionIndex)
        {
            return progress.ConditionProgress.TryGetValue(conditionIndex, out var value) && value > 0;
        }

        public override float GetProgress(AchievementProgress progress, int conditionIndex)
        {
            return IsCompleted(progress, conditionIndex) ? 1f : 0f;
        }

        public override string GetProgressText(AchievementProgress progress, int conditionIndex)
        {
            return IsCompleted(progress, conditionIndex) ? "已完成" : "未完成";
        }

        public override void ProcessEvent(string eventId, object data, AchievementProgress progress, int conditionIndex)
        {
            if (eventId != EventId) return;

            if (RequireData && !string.IsNullOrEmpty(RequiredDataValue))
            {
                if (data?.ToString() != RequiredDataValue)
                    return;
            }

            progress.ConditionProgress[conditionIndex] = 1;
        }
    }

    /// <summary>
    /// 组合条件
    /// </summary>
    [Serializable]
    public class CompositeAchievementCondition : AchievementCondition
    {
        public enum LogicalOperator { And, Or }

        [Tooltip("逻辑运算符")]
        public LogicalOperator Operator = LogicalOperator.And;

        [Tooltip("子条件")]
        [SerializeReference]
        public AchievementCondition[] SubConditions;

        public override string Description =>
            $"({SubConditions?.Length ?? 0} 个条件 {Operator})";

        public override bool IsCompleted(AchievementProgress progress, int conditionIndex)
        {
            if (SubConditions == null || SubConditions.Length == 0) return true;

            if (Operator == LogicalOperator.And)
            {
                for (int i = 0; i < SubConditions.Length; i++)
                {
                    // 子条件使用偏移索引
                    int subIndex = conditionIndex * 100 + i;
                    if (SubConditions[i] != null && !SubConditions[i].IsCompleted(progress, subIndex))
                        return false;
                }
                return true;
            }
            else
            {
                for (int i = 0; i < SubConditions.Length; i++)
                {
                    int subIndex = conditionIndex * 100 + i;
                    if (SubConditions[i] != null && SubConditions[i].IsCompleted(progress, subIndex))
                        return true;
                }
                return false;
            }
        }

        public override float GetProgress(AchievementProgress progress, int conditionIndex)
        {
            if (SubConditions == null || SubConditions.Length == 0) return 1f;

            float total = 0;
            for (int i = 0; i < SubConditions.Length; i++)
            {
                int subIndex = conditionIndex * 100 + i;
                if (SubConditions[i] != null)
                {
                    total += SubConditions[i].GetProgress(progress, subIndex);
                }
            }
            return total / SubConditions.Length;
        }

        public override string GetProgressText(AchievementProgress progress, int conditionIndex)
        {
            int completed = 0;
            for (int i = 0; i < (SubConditions?.Length ?? 0); i++)
            {
                int subIndex = conditionIndex * 100 + i;
                if (SubConditions[i] != null && SubConditions[i].IsCompleted(progress, subIndex))
                {
                    completed++;
                }
            }
            return $"{completed}/{SubConditions?.Length ?? 0}";
        }

        public override void ProcessEvent(string eventId, object data, AchievementProgress progress, int conditionIndex)
        {
            if (SubConditions == null) return;

            for (int i = 0; i < SubConditions.Length; i++)
            {
                int subIndex = conditionIndex * 100 + i;
                SubConditions[i]?.ProcessEvent(eventId, data, progress, subIndex);
            }
        }
    }
}
