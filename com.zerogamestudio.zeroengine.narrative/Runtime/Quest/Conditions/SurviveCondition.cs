using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 存活条件，响应 SurviveCompleted 事件。
    /// </summary>
    [Serializable]
    public class SurviveCondition : QuestCondition
    {
        [Header("Survive")]
        [Tooltip("Stage or encounter ID to match against Quest.SurviveCompleted events. Empty may match any stage.")]
        public string StageId;

        [Min(1)]
        [Tooltip("Number of matching survival completion events required.")]
        public int RequiredCount = 1;

        public override string ConditionType => "Survive";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= RequiredCount;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => RequiredCount;

        public override string GetProgressKey() => $"Survive_{StageId}";

        public override string GetProgressText(QuestRuntimeData runtime)
        {
            return IsSatisfied(runtime) ? "已完成" : $"{GetCurrentProgress(runtime)}/{RequiredCount}";
        }

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != QuestEvents.SurviveCompleted) return false;

            if (eventData is ConditionEventData data &&
                (string.IsNullOrEmpty(StageId) || data.TargetId == StageId))
            {
                runtime.AddProgress(GetProgressKey(), data.Amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}
