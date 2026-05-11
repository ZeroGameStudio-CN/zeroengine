namespace ZeroEngine.Quest
{
    public interface IQuestRewardService
    {
        bool Grant(QuestReward reward, QuestConfigSO quest);
    }
}
