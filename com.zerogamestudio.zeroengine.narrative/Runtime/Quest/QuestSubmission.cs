namespace ZeroEngine.Quest
{
    public enum QuestSubmissionStatus
    {
        NotReady,
        Submitted,
        MissingConfig,
        InvalidConfig,
        InProgress,
        RewardFailed,
        StateChanged
    }

    public readonly struct QuestSubmissionResult
    {
        public QuestSubmissionResult(QuestSubmissionStatus status, string questId, string reason = "", string rewardId = "")
        {
            Status = status;
            QuestId = questId;
            Reason = reason;
            RewardId = rewardId;
        }

        public QuestSubmissionStatus Status { get; }
        public string QuestId { get; }
        public string Reason { get; }
        public string RewardId { get; }
        public bool Succeeded => Status == QuestSubmissionStatus.Submitted;
    }

    public readonly struct QuestRewardGrantContext
    {
        public QuestRewardGrantContext(string questId, string instanceId, string rewardId)
        {
            QuestId = questId;
            InstanceId = instanceId;
            RewardId = rewardId;
        }

        public string QuestId { get; }
        public string InstanceId { get; }
        public string RewardId { get; }
        public string ReceiptId => $"{InstanceId}/{RewardId}";
    }
}
