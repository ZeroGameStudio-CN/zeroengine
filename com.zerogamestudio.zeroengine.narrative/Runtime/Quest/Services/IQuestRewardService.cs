namespace ZeroEngine.Quest
{
    public interface IQuestRewardService
    {
        bool Grant(QuestReward reward, QuestConfigSO quest);
    }

    /// <summary>Optional durable grant contract. Persist ReceiptId with the external side effect.</summary>
    public interface IContextualQuestRewardService : IQuestRewardService
    {
        bool Grant(QuestReward reward, QuestConfigSO quest, QuestRewardGrantContext context);
    }
}
