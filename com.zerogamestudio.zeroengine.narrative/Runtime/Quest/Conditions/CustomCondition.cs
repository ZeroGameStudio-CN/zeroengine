using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 通用自定义条件，匹配 eventType + TargetId。
    /// </summary>
    [Serializable]
    public class CustomCondition : QuestCondition
    {
        [Header("Custom Event")]
        [Tooltip("Custom condition event type to listen for.")]
        [QuestEventNameDropdown]
        public string EventType;

        [Tooltip("Optional target ID. Empty matches all targets for this event type.")]
        public string TargetId;

        [Min(1)]
        [Tooltip("Number of matching custom events required.")]
        public int RequiredCount = 1;

        public override string ConditionType => "Custom";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= RequiredCount;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => RequiredCount;

        public override string GetProgressKey() => $"Custom_{EventType}_{TargetId}";

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != EventType) return false;

            if (eventData is ConditionEventData data)
            {
                if (!string.IsNullOrEmpty(TargetId) && data.TargetId != TargetId)
                    return false;

                runtime.AddProgress(GetProgressKey(), data.Amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}
