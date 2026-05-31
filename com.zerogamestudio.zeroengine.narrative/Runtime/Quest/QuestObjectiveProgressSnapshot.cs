namespace ZeroEngine.Quest
{
    public readonly struct QuestObjectiveProgressSnapshot
    {
        public QuestObjectiveProgressSnapshot(
            string objectiveId,
            string targetId,
            int current,
            int target,
            bool completed,
            bool hidden,
            string displayKey)
        {
            ObjectiveId = objectiveId ?? string.Empty;
            TargetId = targetId ?? string.Empty;
            Current = current;
            Target = target;
            Completed = completed;
            Hidden = hidden;
            DisplayKey = displayKey ?? string.Empty;
        }

        public string ObjectiveId { get; }
        public string TargetId { get; }
        public int Current { get; }
        public int Target { get; }
        public bool Completed { get; }
        public bool Hidden { get; }
        public string DisplayKey { get; }
    }
}
