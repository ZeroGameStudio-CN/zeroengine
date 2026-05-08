using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 到达条件。
    /// 用于到达指定位置/区域
    /// </summary>
    [Serializable]
    public class ReachCondition : QuestCondition
    {
        [Header("Reach")]
        [Tooltip("Location or zone ID to match against Quest.LocationReached events.")]
        public string LocationId;

        [Tooltip("Optional target position for projects that perform distance checks.")]
        public Vector3 TargetPosition;

        [Min(0)]
        [Tooltip("Optional trigger radius. 0 means progress is driven by external trigger events.")]
        public float TriggerRadius = 0f;

        public override string ConditionType => "Reach";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= 1;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => 1;

        public override string GetProgressKey() => $"Reach_{LocationId}";

        public override string GetProgressText(QuestRuntimeData runtime)
        {
            return IsSatisfied(runtime) ? "已到达" : "未到达";
        }

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != QuestEvents.LocationReached) return false;

            if (eventData is ConditionEventData data)
            {
                if (data.TargetId == LocationId)
                {
                    runtime.AddProgress(GetProgressKey(), 1, 1);
                    return true;
                }

                // 距离检测
                if (TriggerRadius > 0 && data.Position != Vector3.zero)
                {
                    float distance = Vector3.Distance(data.Position, TargetPosition);
                    if (distance <= TriggerRadius)
                    {
                        runtime.AddProgress(GetProgressKey(), 1, 1);
                        return true;
                    }
                }
            }

            if (eventData is string locationId && locationId == LocationId)
            {
                runtime.AddProgress(GetProgressKey(), 1, 1);
                return true;
            }

            return false;
        }
    }
}
