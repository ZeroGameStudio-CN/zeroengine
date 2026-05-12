using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 击杀条件。
    /// </summary>
    [Serializable]
    public class KillCondition : QuestCondition
    {
        [Header("Kill")]
        [Tooltip("Entity or enemy type ID to match against Quest.EntityKilled events.")]
        [QuestEntityIdDropdown]
        public string TargetId;

        [Min(1)]
        [Tooltip("Number of matching kill events required.")]
        public int RequiredCount = 1;

        public override string ConditionType => "Kill";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= RequiredCount;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => RequiredCount;

        public override string GetProgressKey() => $"Kill_{TargetId}";

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != QuestEvents.EntityKilled) return false;

            if (eventData is ConditionEventData data && data.TargetId == TargetId)
            {
                runtime.AddProgress(GetProgressKey(), data.Amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}
