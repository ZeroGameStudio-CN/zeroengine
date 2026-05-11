using System.Collections.Generic;
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

            foreach (var quest in quests)
            {
                if (quest == null) continue;

                if (string.IsNullOrWhiteSpace(quest.questId))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Quest has no questId"));
                else if (quest.questId != quest.questId.Trim())
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Warning, "QuestId has leading/trailing whitespace"));

                if (quest.Conditions == null || quest.Conditions.Count == 0)
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Error, "Quest has no Conditions"));

                if (quest.autoSubmit && (quest.Rewards == null || quest.Rewards.Count == 0))
                    issues.Add(new QuestValidationIssue(quest, QuestValidationSeverity.Info, "Auto submit quest has no Rewards"));
            }

            return issues;
        }
    }
}
