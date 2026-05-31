using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 交互条件 (v1.2.0+)
    /// 用于对话、检查物体、使用设备等
    /// </summary>
    [Serializable]
    public class InteractCondition : QuestCondition
    {
        [Tooltip("交互目标 ID（NPC ID / 物体 ID）")]
        public string TargetId;

        [Tooltip("需要交互的次数")]
        public int RequiredCount = 1;

        [Tooltip("交互类型")]
        public InteractionType InteractionType = InteractionType.Talk;

        public override string ConditionType => "Interact";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= RequiredCount;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => RequiredCount;

        public override string GetProgressKey() => $"Interact_{InteractionType}_{TargetId}";

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != QuestEvents.Interacted) return false;

            if (eventData is ConditionEventData data && data.TargetId == TargetId)
            {
                runtime.AddProgress(GetProgressKey(), data.Amount, RequiredCount);
                return true;
            }

            if (eventData is string targetId && targetId == TargetId)
            {
                runtime.AddProgress(GetProgressKey(), 1, RequiredCount);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 交互类型 (v1.2.0+)
    /// </summary>
    public enum InteractionType
    {
        Talk,       // 对话
        Examine,    // 检查
        Use,        // 使用
        Activate,   // 激活
        Custom      // 自定义
    }
}