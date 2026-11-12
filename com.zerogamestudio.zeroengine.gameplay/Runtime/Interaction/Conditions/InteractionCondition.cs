using System;
using UnityEngine;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互条件基类 (v1.14.0+)
    /// 使用 [SerializeReference] 实现多态序列化
    /// </summary>
    [Serializable]
    public abstract class InteractionCondition
    {
        /// <summary>条件类型名称</summary>
        public abstract string ConditionType { get; }

        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        /// <param name="ctx">交互上下文</param>
        /// <param name="interactable">交互目标</param>
        /// <returns>条件是否满足</returns>
        public abstract bool IsSatisfied(InteractionContext ctx, IInteractable interactable);

        /// <summary>
        /// 获取条件不满足时的失败原因
        /// </summary>
        /// <param name="ctx">交互上下文</param>
        /// <param name="interactable">交互目标</param>
        /// <returns>失败原因</returns>
        public abstract string GetFailureReason(InteractionContext ctx, IInteractable interactable);
    }

    /// <summary>
    /// 需要道具条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class ItemRequiredCondition : InteractionCondition
    {
        [Tooltip("所需道具 ID")]
        public string RequiredItemId;

        [Tooltip("所需数量")]
        public int RequiredAmount = 1;

        [Tooltip("交互时是否消耗道具")]
        public bool ConsumeOnInteract = false;

        public override string ConditionType => "ItemRequired";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            // 需要与 InventoryManager 集成
            // 这里提供默认实现，实际项目中应该检查背包
#if ZEROENGINE_INVENTORY
            var inventory = ZeroEngine.Inventory.InventoryManager.Instance;
            if (inventory != null)
            {
                return inventory.HasItem(RequiredItemId, RequiredAmount);
            }
#endif
            return true; // 无背包系统时默认通过
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            return $"Requires {RequiredAmount}x {RequiredItemId}";
        }
    }

    /// <summary>
    /// 任务状态条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class QuestStateCondition : InteractionCondition
    {
        public enum QuestRequirement
        {
            NotStarted,
            InProgress,
            Completed,
            Any
        }

        [Tooltip("任务 ID")]
        public string QuestId;

        [Tooltip("所需状态")]
        public QuestRequirement RequiredState = QuestRequirement.Any;

        public override string ConditionType => "QuestState";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            // 需要与 QuestManager 集成
#if ZEROENGINE_QUEST
            var quest = ZeroEngine.Quest.QuestManager.Instance;
            if (quest != null)
            {
                var state = quest.GetQuestState(QuestId);
                return RequiredState switch
                {
                    QuestRequirement.NotStarted => state == Quest.QuestState.NotStarted,
                    QuestRequirement.InProgress => state == Quest.QuestState.Active,
                    QuestRequirement.Completed => state == Quest.QuestState.Completed,
                    QuestRequirement.Any => true,
                    _ => true
                };
            }
#endif
            return true;
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            return RequiredState switch
            {
                QuestRequirement.NotStarted => $"Quest '{QuestId}' must not be started",
                QuestRequirement.InProgress => $"Quest '{QuestId}' must be in progress",
                QuestRequirement.Completed => $"Quest '{QuestId}' must be completed",
                _ => "Quest requirement not met"
            };
        }
    }

    /// <summary>
    /// 时间条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class TimeCondition : InteractionCondition
    {
        [Tooltip("最早可交互时间 (小时, 0-24)")]
        [Range(0, 24)]
        public float StartHour = 0f;

        [Tooltip("最晚可交互时间 (小时, 0-24)")]
        [Range(0, 24)]
        public float EndHour = 24f;

        public override string ConditionType => "Time";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            // 需要与 TimeManager 集成
#if ZEROENGINE_ENVIRONMENT
            var timeManager = ZeroEngine.EnvironmentSystem.TimeManager.Instance;
            if (timeManager != null)
            {
                float currentHour = timeManager.CurrentHour;

                // 处理跨午夜的时间范围
                if (StartHour <= EndHour)
                {
                    return currentHour >= StartHour && currentHour <= EndHour;
                }
                else
                {
                    // 例如: 22:00 - 06:00
                    return currentHour >= StartHour || currentHour <= EndHour;
                }
            }
#endif
            return true;
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            return $"Available from {StartHour:F0}:00 to {EndHour:F0}:00";
        }
    }

    /// <summary>
    /// 好感度条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class RelationshipCondition : InteractionCondition
    {
        [Tooltip("NPC ID")]
        public string NpcId;

        [Tooltip("最低好感度等级")]
        public int MinLevel = 0;

        public override string ConditionType => "Relationship";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            return !string.IsNullOrWhiteSpace(NpcId) && MinLevel >= 0
                && ctx.Relationships != null && ctx.Relationships.TryGetLevel(NpcId, out var level)
                && level >= 0 && level >= MinLevel;
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            return $"Requires relationship level {MinLevel}+ with {NpcId}";
        }
    }

    /// <summary>
    /// 距离条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class DistanceCondition : InteractionCondition
    {
        [Tooltip("最大交互距离")]
        public float MaxDistance = 3f;

        public override string ConditionType => "Distance";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            return ctx.Distance <= MaxDistance;
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            return "Too far away";
        }
    }

    /// <summary>
    /// 冷却条件 (v1.14.0+)
    /// </summary>
    [Serializable]
    public class CooldownCondition : InteractionCondition
    {
        [Tooltip("冷却时间 (秒)")]
        public float CooldownDuration = 1f;

        // 内部状态 (运行时)
        [NonSerialized]
        private float _lastInteractionTime = float.MinValue;

        public override string ConditionType => "Cooldown";

        public override bool IsSatisfied(InteractionContext ctx, IInteractable interactable)
        {
            return Time.time >= _lastInteractionTime + CooldownDuration;
        }

        public override string GetFailureReason(InteractionContext ctx, IInteractable interactable)
        {
            float remaining = (_lastInteractionTime + CooldownDuration) - Time.time;
            return $"Cooldown: {remaining:F1}s";
        }

        /// <summary>
        /// 标记交互完成 (重置冷却)
        /// </summary>
        public void MarkInteracted()
        {
            _lastInteractionTime = Time.time;
        }
    }
}
