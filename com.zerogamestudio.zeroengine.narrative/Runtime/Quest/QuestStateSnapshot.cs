using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    public readonly struct QuestStateSnapshot
    {
        public QuestStateSnapshot(
            string questId,
            QuestState state,
            IReadOnlyList<QuestObjectiveProgressSnapshot> objectives,
            string submitTargetId,
            QuestLifecycle trackingPolicy,
            IReadOnlyList<string> rewardPreview)
        {
            QuestId = questId ?? string.Empty;
            State = state;
            Objectives = objectives ?? System.Array.Empty<QuestObjectiveProgressSnapshot>();
            SubmitTargetId = submitTargetId ?? string.Empty;
            TrackingPolicy = trackingPolicy;
            RewardPreview = rewardPreview ?? System.Array.Empty<string>();
        }

        public string QuestId { get; }
        public QuestState State { get; }
        public IReadOnlyList<QuestObjectiveProgressSnapshot> Objectives { get; }
        public string SubmitTargetId { get; }
        public QuestLifecycle TrackingPolicy { get; }
        public IReadOnlyList<string> RewardPreview { get; }
    }
}
