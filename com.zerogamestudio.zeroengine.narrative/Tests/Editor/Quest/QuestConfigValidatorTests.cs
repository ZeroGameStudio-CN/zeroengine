using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Quest.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.Quest.Tests.Editor
{
    public sealed class QuestConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingQuestConfigIssues()
        {
            var first = ScriptableObject.CreateInstance<QuestConfigSO>();
            var second = ScriptableObject.CreateInstance<QuestConfigSO>();
            try
            {
                first.name = "FirstQuest";
                first.questId = " quest_a ";
                first.Conditions.Add(null);
                first.Conditions.Add(new KillCondition
                {
                    RequiredCount = 0
                });
                first.AcceptRequirements.Add(new QuestStateAcceptRequirement());
                first.Rewards.Add(new ItemReward
                {
                    Quantity = 0
                });

                second.name = "SecondQuest";
                second.questId = "quest_a";
                second.questName = "Quest A";
                second.Conditions.Add(new SurviveCondition
                {
                    Description = "Survive once",
                    RequiredCount = 1
                });

                var issues = QuestConfigValidator.Validate(new[] { first, second });

                AssertIssue(issues, first, QuestValidationSeverity.Warning, "QuestId has leading/trailing whitespace");
                AssertIssue(issues, first, QuestValidationSeverity.Warning, "Quest has no designer-facing questName");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "Quest Condition is empty");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "KillCondition TargetId is empty");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "KillCondition RequiredCount must be greater than 0");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "QuestStateAcceptRequirement questId is empty");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "ItemReward ItemId is empty");
                AssertIssue(issues, first, QuestValidationSeverity.Error, "ItemReward Quantity must be greater than 0");
                Assert.That(issues.Count(issue => issue.Message.Contains("duplicated")), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static void AssertIssue(
            System.Collections.Generic.IEnumerable<QuestValidationIssue> issues,
            QuestConfigSO quest,
            QuestValidationSeverity severity,
            string message)
        {
            Assert.That(
                issues.Any(issue =>
                    issue.Quest == quest &&
                    issue.Asset == quest &&
                    issue.Severity == severity &&
                    issue.Message == message),
                Is.True,
                message);
        }
    }
}
