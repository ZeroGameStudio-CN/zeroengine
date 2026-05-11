using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    public static class QuestServiceRegistry
    {
        private static readonly IQuestConfigSource DefaultConfigSource = new EmptyQuestConfigSource();
        private static readonly IQuestRewardService DefaultRewardService = new DirectQuestRewardService();
        private static readonly IQuestLocalizationService DefaultLocalizationService = new FallbackQuestLocalizationService();

        public static IQuestConfigSource ConfigSource { get; private set; } = DefaultConfigSource;
        public static IQuestRewardService RewardService { get; private set; } = DefaultRewardService;
        public static IQuestLocalizationService LocalizationService { get; private set; } = DefaultLocalizationService;
        public static bool HasCustomConfigSource { get; private set; }

        public static void SetConfigSource(IQuestConfigSource source)
        {
            ConfigSource = source ?? DefaultConfigSource;
            HasCustomConfigSource = source != null;
        }

        public static void SetRewardService(IQuestRewardService service) => RewardService = service ?? DefaultRewardService;
        public static void SetLocalizationService(IQuestLocalizationService service) => LocalizationService = service ?? DefaultLocalizationService;

        public static void ResetForTests()
        {
            ConfigSource = DefaultConfigSource;
            RewardService = DefaultRewardService;
            LocalizationService = DefaultLocalizationService;
            HasCustomConfigSource = false;
        }

        private sealed class EmptyQuestConfigSource : IQuestConfigSource
        {
            public IReadOnlyList<QuestConfigSO> LoadConfigs() => new List<QuestConfigSO>();
        }

        private sealed class DirectQuestRewardService : IQuestRewardService
        {
            public bool Grant(QuestReward reward, QuestConfigSO quest) => reward == null || reward.Grant();
        }

        private sealed class FallbackQuestLocalizationService : IQuestLocalizationService
        {
            public string GetQuestTitle(QuestConfigSO quest) => quest == null ? string.Empty : quest.questName;
            public string GetQuestDescription(QuestConfigSO quest) => quest == null ? string.Empty : quest.description;
            public string GetConditionText(QuestConfigSO quest, QuestCondition condition) => condition == null ? string.Empty : condition.Description;
            public string GetRewardText(QuestConfigSO quest, QuestReward reward) => reward == null ? string.Empty : reward.GetPreviewText();
        }
    }
}
