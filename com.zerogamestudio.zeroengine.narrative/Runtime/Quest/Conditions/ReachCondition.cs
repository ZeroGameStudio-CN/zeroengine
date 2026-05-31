using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 到达条件 (v1.2.0+)
    /// 用于到达指定位置/区域
    /// </summary>
    [Serializable]
    public class ReachCondition : QuestCondition
    {
        [Tooltip("区域/位置 ID")]
        public string LocationId;

        [Tooltip("目标位置（可选，用于距离检测）")]
        public Vector3 TargetPosition;

        [Tooltip("触发半径（0 表示使用区域触发器）")]
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