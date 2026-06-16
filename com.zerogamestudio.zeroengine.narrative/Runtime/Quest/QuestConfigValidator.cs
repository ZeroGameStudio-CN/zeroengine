using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    public sealed class QuestValidationOptions
    {
        public static QuestValidationOptions Default { get; } = new QuestValidationOptions();
    }

    public readonly struct QuestValidationIssue
    {
        public QuestValidationIssue(string code, string questId, string message)
        {
            Code = code ?? string.Empty;
            QuestId = questId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Code { get; }
        public string QuestId { get; }
        public string Message { get; }
    }

    public static class QuestConfigValidator
    {
        public static List<QuestValidationIssue> Validate(QuestConfigSO config, QuestValidationOptions options = null)
        {
            options ??= QuestValidationOptions.Default;
            var issues = new List<QuestValidationIssue>();
            if (config == null)
            {
                issues.Add(new QuestValidationIssue("quest.config_null", string.Empty, "Quest config is null."));
                return issues;
            }

            if (string.IsNullOrEmpty(config.questId))
            {
                issues.Add(new QuestValidationIssue("quest.id_missing", config.questId, "Quest id is missing."));
            }

            if (config.UsesNewConditionSystem)
            {
                foreach (var condition in config.Conditions)
                {
                    if (condition == null)
                    {
                        issues.Add(new QuestValidationIssue("quest.condition_null", config.questId, "Quest contains a null condition."));
                    }
                }
            }

            if (config.Rewards != null)
            {
                foreach (var reward in config.Rewards)
                {
                    if (reward == null)
                    {
                        issues.Add(new QuestValidationIssue("quest.reward_null", config.questId, "Quest contains a null reward."));
                    }
                }
            }

            return issues;
        }
    }
}
