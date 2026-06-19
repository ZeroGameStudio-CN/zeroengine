using System.Collections.Generic;
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
        public readonly string FieldPath;
        public readonly string Message;

        public QuestConfigSO Asset => Quest;

        public QuestValidationIssue(QuestConfigSO quest, QuestValidationSeverity severity, string message)
            : this(quest, severity, string.Empty, message)
        {
        }

        public QuestValidationIssue(QuestConfigSO quest, QuestValidationSeverity severity, string fieldPath, string message)
        {
            Quest = quest;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
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

            var materializedQuests = quests.Where(quest => quest != null).ToArray();
            foreach (var quest in materializedQuests)
            {
                if (string.IsNullOrWhiteSpace(quest.questId))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, nameof(QuestConfigSO.questId), "Quest has no questId"));
                else if (quest.questId != quest.questId.Trim())
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, nameof(QuestConfigSO.questId), "QuestId has leading/trailing whitespace"));

                if (string.IsNullOrWhiteSpace(quest.questName))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, nameof(QuestConfigSO.questName), "Quest has no designer-facing questName"));

                if (quest.Conditions == null || quest.Conditions.Count == 0)
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, nameof(QuestConfigSO.Conditions), "Quest has no Conditions"));
                else
                    ValidateConditions(quest, issues);

                if (quest.autoSubmit && (quest.Rewards == null || quest.Rewards.Count == 0))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Info, nameof(QuestConfigSO.Rewards), "Auto submit quest has no Rewards"));

                ValidateAcceptRequirements(quest, issues);
                ValidateRewards(quest, issues);
            }

            AddDuplicateQuestIdIssues(materializedQuests, issues);
            return issues;
        }

        private static void ValidateConditions(QuestConfigSO quest, ICollection<QuestValidationIssue> issues)
        {
            for (var i = 0; i < quest.Conditions.Count; i++)
            {
                var condition = quest.Conditions[i];
                var fieldPath = $"{nameof(QuestConfigSO.Conditions)}[{i}]";
                if (condition == null)
                {
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, "Quest Condition is empty"));
                    continue;
                }

                if (!condition.IsHidden && string.IsNullOrWhiteSpace(condition.Description))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, fieldPath, "Visible Quest Condition has no Description"));

                switch (condition)
                {
                    case KillCondition kill:
                        RequireId(quest, issues, fieldPath, kill.TargetId, "KillCondition TargetId");
                        RequirePositive(quest, issues, fieldPath, kill.RequiredCount, "KillCondition RequiredCount");
                        break;
                    case CollectCondition collect:
                        RequireId(quest, issues, fieldPath, collect.ItemId, "CollectCondition ItemId");
                        RequirePositive(quest, issues, fieldPath, collect.RequiredCount, "CollectCondition RequiredCount");
                        break;
                    case InteractCondition interact:
                        RequireId(quest, issues, fieldPath, interact.TargetId, "InteractCondition TargetId");
                        RequirePositive(quest, issues, fieldPath, interact.RequiredCount, "InteractCondition RequiredCount");
                        break;
                    case ReachCondition reach:
                        RequireId(quest, issues, fieldPath, reach.LocationId, "ReachCondition LocationId");
                        if (reach.TriggerRadius < 0f)
                            issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, "ReachCondition TriggerRadius must not be negative"));
                        break;
                    case SurviveCondition survive:
                        RequirePositive(quest, issues, fieldPath, survive.RequiredCount, "SurviveCondition RequiredCount");
                        break;
                    case CustomCondition custom:
                        RequireId(quest, issues, fieldPath, custom.EventType, "CustomCondition EventType");
                        RequirePositive(quest, issues, fieldPath, custom.RequiredCount, "CustomCondition RequiredCount");
                        break;
                }
            }
        }

        private static void ValidateAcceptRequirements(QuestConfigSO quest, ICollection<QuestValidationIssue> issues)
        {
            if (quest.AcceptRequirements == null) return;

            for (var i = 0; i < quest.AcceptRequirements.Count; i++)
            {
                var requirement = quest.AcceptRequirements[i];
                var fieldPath = $"{nameof(QuestConfigSO.AcceptRequirements)}[{i}]";
                if (requirement == null)
                {
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, "Quest AcceptRequirement is empty"));
                    continue;
                }

                if (requirement is QuestStateAcceptRequirement stateRequirement)
                    RequireId(quest, issues, fieldPath, stateRequirement.questId, "QuestStateAcceptRequirement questId");
            }
        }

        private static void ValidateRewards(QuestConfigSO quest, ICollection<QuestValidationIssue> issues)
        {
            if (quest.Rewards == null) return;

            for (var i = 0; i < quest.Rewards.Count; i++)
            {
                var reward = quest.Rewards[i];
                var fieldPath = $"{nameof(QuestConfigSO.Rewards)}[{i}]";
                if (reward == null)
                {
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, "Quest Reward is empty"));
                    continue;
                }

                if (!reward.IsHidden && string.IsNullOrWhiteSpace(reward.Description))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, fieldPath, "Visible Quest Reward has no Description"));

                switch (reward)
                {
                    case ItemReward item:
                        RequireId(quest, issues, fieldPath, item.ItemId, "ItemReward ItemId");
                        RequirePositive(quest, issues, fieldPath, item.Quantity, "ItemReward Quantity");
                        break;
                    case CurrencyReward currency:
                        RequirePositive(quest, issues, fieldPath, currency.Amount, "CurrencyReward Amount");
                        break;
                    case ExpReward exp:
                        RequirePositive(quest, issues, fieldPath, exp.Amount, "ExpReward Amount");
                        break;
                }
            }
        }

        private static void AddDuplicateQuestIdIssues(
            IEnumerable<QuestConfigSO> quests,
            ICollection<QuestValidationIssue> issues)
        {
            foreach (var duplicateGroup in quests
                         .Where(quest => !string.IsNullOrWhiteSpace(quest.questId))
                         .GroupBy(quest => quest.questId.Trim(), System.StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1) continue;

                foreach (var duplicate in duplicates)
                    issues.Add(new QuestValidationIssue(
                        duplicate,
                        QuestValidationSeverity.Error,
                        nameof(QuestConfigSO.questId),
                        $"QuestId '{duplicateGroup.Key}' is duplicated in {duplicates.Length} quests"));
            }
        }

        private static void RequireId(
            QuestConfigSO quest,
            ICollection<QuestValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, $"{label} is empty"));
            else if (value != value.Trim())
                issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace"));
        }

        private static void RequirePositive(
            QuestConfigSO quest,
            ICollection<QuestValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
                issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, fieldPath, $"{label} must be greater than 0"));
        }
    }
}
