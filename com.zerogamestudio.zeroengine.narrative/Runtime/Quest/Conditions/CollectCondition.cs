using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 收集条件。
    /// </summary>
    [Serializable]
    public class CollectCondition : QuestCondition
    {
        [Header("Collect")]
        [Tooltip("Item ID to match against Quest.ItemObtained events.")]
        [QuestItemIdDropdown]
        public string ItemId;

        [Min(1)]
        [Tooltip("Number of matching item events required.")]
        public int RequiredCount = 1;

        [Tooltip("Whether the project should consume the item when the quest is completed. Consumption is project-defined.")]
        public bool ConsumeOnComplete = true;

        public override string ConditionType => "Collect";

        public override bool IsSatisfied(QuestRuntimeData runtime)
        {
            return GetCurrentProgress(runtime) >= RequiredCount;
        }

        public override int GetCurrentProgress(QuestRuntimeData runtime)
        {
            return runtime.GetProgress(GetProgressKey());
        }

        public override int GetTargetProgress() => RequiredCount;

        public override string GetProgressKey() => $"Collect_{ItemId}";

        public override bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData)
        {
            if (eventType != QuestEvents.ItemObtained) return false;

            if (eventData is ConditionEventData data && data.TargetId == ItemId)
            {
                runtime.AddProgress(GetProgressKey(), data.Amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}
