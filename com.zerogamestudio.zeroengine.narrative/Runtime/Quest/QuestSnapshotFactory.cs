using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    public static class QuestSnapshotFactory
    {
        public static QuestStateSnapshot Create(QuestRuntimeData runtime, QuestConfigSO config)
        {
            var objectives = new List<QuestObjectiveProgressSnapshot>();
            if (runtime != null && config != null && config.Conditions != null)
            {
                foreach (var condition in config.Conditions)
                {
                    if (condition == null)
                    {
                        continue;
                    }

                    var current = condition.GetCurrentProgress(runtime);
                    var target = condition.GetTargetProgress();
                    objectives.Add(new QuestObjectiveProgressSnapshot(
                        condition.GetProgressKey(),
                        GetTargetId(condition),
                        current,
                        target,
                        condition.IsSatisfied(runtime),
                        condition.IsHidden,
                        string.IsNullOrEmpty(condition.Description) ? condition.GetProgressKey() : condition.Description));
                }
            }

            return new QuestStateSnapshot(
                runtime?.questId ?? config?.questId,
                runtime?.state ?? QuestState.Inactive,
                objectives,
                config?.submitNpcId,
                config?.lifecycle ?? QuestLifecycle.Persistent,
                config?.GetRewardPreviews());
        }

        private static string GetTargetId(QuestCondition condition)
        {
            return condition switch
            {
                CollectCondition collect => collect.ItemId,
                KillCondition kill => kill.TargetId,
                InteractCondition interact => interact.TargetId,
                ReachCondition reach => reach.LocationId,
                CustomCondition custom => custom.TargetId,
                _ => condition.GetProgressKey()
            };
        }
    }
}
