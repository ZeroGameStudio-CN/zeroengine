using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 击杀条件 (v1.2.0+)
    /// </summary>
    [Serializable]
    public class KillCondition : QuestCondition
    {
        [Tooltip("目标 ID（敌人类型 ID）")]
        public string TargetId;

        [Tooltip("需要击杀的数量")]
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

            // Legacy support for string, int
            if (eventData is (string targetId, int amount) && targetId == TargetId)
            {
                runtime.AddProgress(GetProgressKey(), amount, RequiredCount);
                return true;
            }

            return false;
        }
    }
}