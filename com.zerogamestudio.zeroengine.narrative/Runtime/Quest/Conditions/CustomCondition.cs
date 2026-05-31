using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 通用自定义条件 — 匹配 eventType + TargetId (v1.3.0+)
    /// </summary>
    [Serializable]
    public class CustomCondition : QuestCondition
    {
        [Tooltip("要监听的事件类型")]
        public string EventType;

        [Tooltip("目标 ID（留空则匹配所有）")]
        public string TargetId;

        [Tooltip("需要的次数")]
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
