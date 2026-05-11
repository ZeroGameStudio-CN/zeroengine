namespace ZeroEngine.Quest
{
    public interface IQuestLocalizationService
    {
        string GetQuestTitle(QuestConfigSO quest);
        string GetQuestDescription(QuestConfigSO quest);
        string GetConditionText(QuestConfigSO quest, QuestCondition condition);
        string GetRewardText(QuestConfigSO quest, QuestReward reward);
    }
}
