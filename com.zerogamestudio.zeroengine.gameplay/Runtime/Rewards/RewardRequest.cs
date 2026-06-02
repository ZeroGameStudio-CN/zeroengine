namespace ZeroEngine.Gameplay.Rewards
{
    public readonly struct RewardRequest
    {
        public RewardRequest(
            string rewardId,
            string rewardType,
            string sourceId,
            string payloadId,
            int quantity = 1)
        {
            RewardId = rewardId ?? string.Empty;
            RewardType = rewardType ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            PayloadId = payloadId ?? string.Empty;
            Quantity = quantity <= 0 ? 1 : quantity;
        }

        public string RewardId { get; }
        public string RewardType { get; }
        public string SourceId { get; }
        public string PayloadId { get; }
        public int Quantity { get; }
    }
}
