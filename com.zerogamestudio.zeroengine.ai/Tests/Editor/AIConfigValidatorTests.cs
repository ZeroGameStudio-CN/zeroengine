using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AI.Editor;
using ZeroEngine.AI.NPCSchedule;
using ZeroEngine.BehaviorTree;
using Object = UnityEngine.Object;

namespace ZeroEngine.AI.Editor.Tests
{
    public sealed class AIConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsBrokenScheduleAndBehaviorTreeConfig()
        {
            var schedule = ScriptableObject.CreateInstance<NPCScheduleSO>();
            var preset = ScriptableObject.CreateInstance<SchedulePresetSO>();
            var tree = ScriptableObject.CreateInstance<BTTreeAsset>();

            try
            {
                schedule.name = "InvalidSchedule";
                schedule.AddEntry(new ScheduleEntry());

                preset.name = "InvalidPreset";

                tree.name = "InvalidTree";
                tree.DisplayName = string.Empty;
                tree.Description = string.Empty;
                tree.RootNodeId = "missing";
                tree.Nodes.Add(new BTNodeData
                {
                    Id = "root",
                    Name = string.Empty,
                    Type = BTNodeType.Wait,
                    WaitDuration = 0f
                });
                tree.Nodes.Add(new BTNodeData
                {
                    Id = "root",
                    Name = "Duplicate",
                    Type = BTNodeType.Log,
                    LogMessage = string.Empty
                });

                var issues = AIConfigValidator.Validate(new[] { schedule }, new[] { preset }, new[] { tree });

                AssertError(issues, "NPC schedule must have a stable ID.");
                AssertError(issues, "Schedule entry must have a stable ID.");
                AssertError(issues, "Schedule entry must define an action.");
                AssertError(issues, "Schedule preset must have a stable ID.");
                AssertError(issues, "Behavior tree must have a display name.");
                AssertError(issues, "Root node 'missing' does not exist.");
                AssertError(issues, "Behavior tree node must have a display name.");
                AssertError(issues, "Duplicate behavior tree node ID 'root'.");
                AssertError(issues, "Wait node duration must be positive.");
            }
            finally
            {
                Object.DestroyImmediate(schedule);
                Object.DestroyImmediate(preset);
                Object.DestroyImmediate(tree);
            }
        }

        private static void AssertError(IReadOnlyList<AIValidationIssue> issues, string expectedMessage)
        {
            Assert.That(
                issues.Any(issue => issue.Severity == AIValidationSeverity.Error && issue.Message == expectedMessage),
                Is.True,
                $"Expected validation error '{expectedMessage}', got:\n{string.Join("\n", issues.Select(issue => issue.ToString()))}");
        }
    }
}
