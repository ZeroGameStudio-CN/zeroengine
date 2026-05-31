using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 收集条件 (v1.2.0+)
    /// </summary>
    [Serializable]
    public class CollectCondition : QuestCondition
    {
        [Tooltip("物品 ID")]
        public string ItemId;

        [Tooltip("需要收集的数量")]
        public int RequiredCount = 1;

        [Tooltip("是否在完成时消耗物品")]
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

            if (eventData is (string itemId, int amount) && itemId == ItemId)
            {
                runtime.AddProgress(GetProgressKey(), amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}