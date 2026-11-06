using System;
using UnityEngine;

using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就条件基类
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementCondition")]
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
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "CounterCondition")]
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
    /// 事件条件（一次性触发）
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "EventCondition")]
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
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "CompositeAchievementCondition")]
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
