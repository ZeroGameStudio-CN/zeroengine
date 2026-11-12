using System;
using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    [Serializable]
    public readonly struct QuestObjectiveSnapshot
    {
        public QuestObjectiveSnapshot(
            string objectiveId,
            string targetId,
            string displayKey,
            int current,
            int target,
            bool completed,
            bool hidden)
        {
            ObjectiveId = objectiveId;
            TargetId = targetId;
            DisplayKey = displayKey;
            Current = current;
            Target = target;
            Completed = completed;
            Hidden = hidden;
        }

        public string ObjectiveId { get; }
        public string TargetId { get; }
        public string DisplayKey { get; }
        public int Current { get; }
        public int Target { get; }
        public bool Completed { get; }
        public bool Hidden { get; }
    }

    [Serializable]
    public readonly struct QuestStateSnapshot
    {
        public QuestStateSnapshot(
            string questId,
            QuestState state,
            IReadOnlyList<QuestObjectiveSnapshot> objectives,
            IReadOnlyList<string> rewardPreview)
        {
            QuestId = questId;
            State = state;
            Objectives = objectives;
            RewardPreview = rewardPreview;
        }

        public string QuestId { get; }
        public QuestState State { get; }
        public IReadOnlyList<QuestObjectiveSnapshot> Objectives { get; }
        public IReadOnlyList<string> RewardPreview { get; }
    }
}
