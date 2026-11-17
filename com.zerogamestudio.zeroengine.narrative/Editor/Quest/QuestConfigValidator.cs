using System.Collections.Generic;
using System;
using System.Linq;
using UnityEditor;

namespace ZeroEngine.Quest.Editor
{
    public enum QuestValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct QuestValidationIssue
    {
        public readonly QuestConfigSO Quest;
        public readonly QuestValidationSeverity Severity;
        public readonly string Message;

        public QuestValidationIssue(QuestConfigSO quest, QuestValidationSeverity severity, string message)
        {
            Quest = quest;
            Severity = severity;
            Message = message;
        }
    }

    public static class QuestConfigValidator
    {
        public static IReadOnlyList<QuestConfigSO> LoadQuestAssets()
        {
            var result = new List<QuestConfigSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:QuestConfigSO"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var quest = AssetDatabase.LoadAssetAtPath<QuestConfigSO>(path);
                if (quest != null)
                    result.Add(quest);
            }

            return result;
        }

        public static IReadOnlyList<QuestValidationIssue> Validate(IEnumerable<QuestConfigSO> quests)
        {
            var issues = new List<QuestValidationIssue>();
            if (quests == null) return issues;

            var configs = quests.Where(quest => quest != null).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in configs)
            {
                if (quest == null) continue;

                if (string.IsNullOrWhiteSpace(quest.questId))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Quest has no questId"));
                else if (quest.questId != quest.questId.Trim())
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, "QuestId has leading/trailing whitespace"));
                if (!string.IsNullOrWhiteSpace(quest.questId) && !ids.Add(quest.questId.Trim()))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Duplicate quest ID"));

                if (quest.Conditions == null || quest.Conditions.Count == 0)
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Quest has no Conditions"));

                if (quest.autoSubmit && (quest.Rewards == null || quest.Rewards.Count == 0))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Info, "Auto submit quest has no Rewards"));

                if (quest.HasLegacyLogic || quest.HasLegacyRewards)
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error,
                        "Legacy fields are not executable. Migrate legacy rewards through the asset context menu and author Conditions explicitly."));
                if (quest.autoActivate && quest.repetitionLimit != 1)
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error,
                        "Auto-activated quests require repetitionLimit=1 (per lifecycle)."));

                var conditionKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var condition in quest.Conditions ?? new List<QuestCondition>())
                {
                    if (condition == null)
                    {
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Null condition"));
                        continue;
                    }
                    if (condition.GetTargetProgress() <= 0)
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Condition target must be positive"));
                    if (!conditionKeys.Add(condition.GetStorageKey()))
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Duplicate condition progress identity"));
                    if (string.IsNullOrWhiteSpace(condition.ConditionId))
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, "Legacy condition identity; assign stable IDs before release"));
                    if (condition is CustomCondition custom && string.IsNullOrWhiteSpace(custom.EventType))
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Custom condition has no event type"));
                }
                var rewardKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reward in quest.Rewards ?? new List<QuestReward>())
                {
                    if (reward == null)
                    {
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Null reward"));
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(reward.RewardId) && !rewardKeys.Add(reward.RewardId.Trim()))
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Duplicate reward identity"));
                    if (string.IsNullOrWhiteSpace(reward.RewardId))
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, "Legacy reward position; assign stable IDs before release"));
                    if (reward is ItemReward item && (string.IsNullOrWhiteSpace(item.ItemId) || item.Quantity <= 0) ||
                        reward is CurrencyReward currency && currency.Amount <= 0 ||
                        reward is ExpReward experience && experience.Amount <= 0)
                        issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Invalid built-in reward payload"));
                }
            }

            return issues;
        }
    }
}
