using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Gameplay.Editor;
using ZeroEngine.Interaction;
using ZeroEngine.Tutorial;
using Object = UnityEngine.Object;

namespace ZeroEngine.Gameplay.Editor.Tests
{
    public sealed class GameplayConfigValidatorTests
    {
        [Test]
        public void Validate_ReportsDesignerBlockingGameplayConfigIssues()
        {
            var interactionConfig = ScriptableObject.CreateInstance<InteractionConfigSO>();
            var tutorialConfig = ScriptableObject.CreateInstance<TutorialConfigSO>();
            var sequenceA = ScriptableObject.CreateInstance<TutorialSequenceSO>();
            var sequenceB = ScriptableObject.CreateInstance<TutorialSequenceSO>();
            var stepA = ScriptableObject.CreateInstance<TutorialStepSO>();
            var stepB = ScriptableObject.CreateInstance<TutorialStepSO>();
            var tutorial = ScriptableObject.CreateInstance<TutorialSO>();
            var group = ScriptableObject.CreateInstance<TutorialGroupSO>();

            try
            {
                interactionConfig.name = "InteractionConfig";
                interactionConfig.DefaultDetectionRadius = 1f;
                interactionConfig.DefaultInteractionDistance = 2f;
                interactionConfig.DetectionRate = 0;
                interactionConfig.UseNewInputSystem = true;
                interactionConfig.InteractActionName = string.Empty;
                interactionConfig.PromptUIPrefab = null;
                interactionConfig.PromptShowDelay = -1f;
                interactionConfig.HintTemplates = new List<InteractionHintTemplate>
                {
                    new InteractionHintTemplate { Type = InteractionType.Pickup, Template = string.Empty },
                    new InteractionHintTemplate { Type = InteractionType.Pickup, Template = "Open" }
                };

                tutorialConfig.name = "TutorialConfig";
                tutorialConfig.EnableTutorials = true;
                tutorialConfig.DialogueUIPrefab = null;
                tutorialConfig.UIFadeInDuration = -1f;
                tutorialConfig.HighlightPulseSpeed = 0f;
                tutorialConfig.DefaultTypewriterSpeed = 0f;
                tutorialConfig.DialogueConfirmKey = KeyCode.Space;
                tutorialConfig.SkipKey = KeyCode.Space;
                tutorialConfig.SoundVolume = 2f;
                tutorialConfig.TargetSearchInterval = 0f;
                tutorialConfig.MaxTargetSearchAttempts = 0;

                sequenceA.name = "SequenceA";
                sequenceA.SequenceId = " intro ";
                sequenceA.DisplayName = string.Empty;
                sequenceA.Description = string.Empty;
                sequenceA.Prerequisites = new[] { "intro", "other", "other" };
                sequenceA.MutuallyExclusive = new[] { string.Empty };
                sequenceA.NextSequenceId = "intro";
                sequenceA.StartConditions = new List<TutorialCondition>
                {
                    null,
                    new LevelCondition { MinLevel = 5, MaxLevel = 1 },
                    new QuestCondition { QuestId = string.Empty },
                    new VariableCondition { VariableKey = string.Empty },
                    new TutorialCompletedCondition { RequiredTutorialIds = new[] { string.Empty } }
                };
                sequenceA.Steps = new List<TutorialStep>
                {
                    null,
                    new DialogueStep
                    {
                        StepId = "step_a",
                        DialogueText = string.Empty,
                        TypewriterSpeed = -1f,
                        WaitForConfirm = true,
                        ConfirmKey = KeyCode.None
                    },
                    new DelayStep
                    {
                        StepId = "step_a",
                        Duration = 0f
                    },
                    new HighlightStep
                    {
                        TargetPath = string.Empty,
                        Timeout = -1f
                    },
                    new CallbackStep
                    {
                        CallbackId = string.Empty
                    },
                    new WaitInputStep
                    {
                        RequiredKeys = System.Array.Empty<KeyCode>(),
                        Timeout = -1f
                    },
                    new WaitEventStep
                    {
                        EventKey = string.Empty,
                        Timeout = -1f
                    },
                    new WaitInteractionStep
                    {
                        InteractableId = string.Empty,
                        InteractionRequirement = InteractionRequirement.Any,
                        Timeout = -1f
                    },
                    new MoveToStep
                    {
                        TargetObjectPath = string.Empty,
                        TargetPosition = Vector3.zero,
                        ArrivalDistance = 0f,
                        Timeout = -1f,
                        ArrivalDelay = -1f
                    },
                    new CompositeStep()
                };
                sequenceA.CompletionRewards = new List<TutorialReward>
                {
                    null,
                    new ItemTutorialReward { ItemId = string.Empty, Amount = 0 },
                    new AchievementTutorialReward { AchievementId = string.Empty }
                };

                sequenceB.name = "SequenceB";
                sequenceB.SequenceId = "intro";
                sequenceB.DisplayName = "Intro";
                sequenceB.Description = "Intro";
                sequenceB.Steps = new List<TutorialStep>
                {
                    new DelayStep { Duration = 1f }
                };

                stepA.name = "StepA";
                stepA.StepId = "step_asset";
                stepA.Title = string.Empty;
                stepA.Description = string.Empty;
                stepA.TriggerType = TriggerType.OnEvent;
                stepA.TriggerEventId = string.Empty;
                stepA.TriggerDelay = -1f;
                stepA.AutoCompleteDelay = -1f;
                stepA.CompleteConditions = new List<StepCondition>
                {
                    null,
                    new StepCondition { Type = ConditionType.HasItem, TargetId = string.Empty, TargetValue = -1 }
                };
                stepA.Highlights = new List<HighlightTarget>
                {
                    null,
                    new HighlightTarget { TargetPath = string.Empty, Scale = 0f }
                };
                stepA.Tooltip = new TooltipConfig { Show = true, Text = string.Empty };
                stepA.OnStartActions = new List<StepAction>
                {
                    null,
                    new StepAction { Type = ActionType.TriggerEvent, ActionId = string.Empty }
                };

                stepB.name = "StepB";
                stepB.StepId = "step_asset";
                stepB.Title = "Step";
                stepB.Description = "Step";

                tutorial.name = "Tutorial";
                tutorial.TutorialId = "tutorial_a";
                tutorial.DisplayName = string.Empty;
                tutorial.Description = string.Empty;
                tutorial.Steps = new List<TutorialStepSO> { null, stepA, stepA };
                tutorial.Prerequisites = new List<TutorialSO> { null, tutorial };

                group.name = "Group";
                group.GroupId = string.Empty;
                group.DisplayName = string.Empty;
                group.Description = string.Empty;
                group.Tutorials = new List<TutorialSO> { null, tutorial, tutorial };

                var issues = GameplayConfigValidator.Validate(
                    new[] { interactionConfig },
                    new[] { tutorialConfig },
                    new[] { sequenceA, sequenceB },
                    new[] { stepA, stepB },
                    new[] { tutorial },
                    new[] { group });

                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Error, "DefaultInteractionDistance must not be greater than DefaultDetectionRadius.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Error, "DetectionRate must be between 1 and 60.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Error, "InteractActionName is empty while UseNewInputSystem is enabled.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Warning, "PromptUIPrefab is not assigned.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Error, "Hint template text is empty.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Error, "Hint template for Pickup is duplicated.");
                AssertIssue(issues, interactionConfig, GameplayValidationSeverity.Warning, "Hint template should include the '{0}' display-name placeholder.");

                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "UIFadeInDuration must not be negative.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "HighlightPulseSpeed must be greater than 0.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "DefaultTypewriterSpeed must be greater than 0.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "SkipKey must be different from DialogueConfirmKey.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "SoundVolume must be between 0 and 1.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "TargetSearchInterval must be greater than 0.");
                AssertIssue(issues, tutorialConfig, GameplayValidationSeverity.Error, "MaxTargetSearchAttempts must be greater than 0.");

                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Warning, "Tutorial sequence ID has leading/trailing whitespace.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Warning, "Tutorial sequence display name is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Tutorial condition is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Level condition MaxLevel is lower than MinLevel.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Quest condition QuestId is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Variable condition key is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Required tutorial ID is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Tutorial sequence step is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "DialogueText is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "ConfirmKey is None while WaitForConfirm is enabled.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Delay Duration must be greater than 0.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Tutorial step ID 'step_a' is duplicated inside the sequence.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "MoveTo needs TargetPosition or TargetObjectPath.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Tutorial sequence has no steps.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Tutorial reward is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Item reward ItemId is empty.");
                AssertIssue(issues, sequenceA, GameplayValidationSeverity.Error, "Achievement reward AchievementId is empty.");
                Assert.That(issues.Count(issue => issue.Message.Contains("Tutorial sequence ID") && issue.Message.Contains("duplicated")), Is.EqualTo(2));

                AssertIssue(issues, stepA, GameplayValidationSeverity.Warning, "Tutorial step title is empty.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "TriggerEventId is empty for OnEvent trigger.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Step condition is empty.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Step condition TargetId is empty.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Highlight target is empty.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Highlight scale must be greater than 0.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Warning, "Tooltip text is empty while tooltip is shown.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Step action is empty.");
                AssertIssue(issues, stepA, GameplayValidationSeverity.Error, "Step action ActionId is empty.");
                Assert.That(issues.Count(issue => issue.Message == "Tutorial step ID 'step_asset' is duplicated in 2 assets."), Is.EqualTo(2));

                AssertIssue(issues, tutorial, GameplayValidationSeverity.Warning, "Tutorial display name is empty.");
                AssertIssue(issues, tutorial, GameplayValidationSeverity.Error, "Tutorial step asset is missing.");
                AssertIssue(issues, tutorial, GameplayValidationSeverity.Error, "Tutorial contains a duplicate step asset reference.");
                AssertIssue(issues, tutorial, GameplayValidationSeverity.Warning, "Tutorial prerequisite is missing.");
                AssertIssue(issues, tutorial, GameplayValidationSeverity.Error, "Tutorial must not require itself as a prerequisite.");

                AssertIssue(issues, group, GameplayValidationSeverity.Error, "Tutorial group ID is empty.");
                AssertIssue(issues, group, GameplayValidationSeverity.Warning, "Tutorial group display name is empty.");
                AssertIssue(issues, group, GameplayValidationSeverity.Error, "Tutorial group entry is missing.");
                AssertIssue(issues, group, GameplayValidationSeverity.Error, "Tutorial group contains a duplicate tutorial reference.");
            }
            finally
            {
                Object.DestroyImmediate(interactionConfig);
                Object.DestroyImmediate(tutorialConfig);
                Object.DestroyImmediate(sequenceA);
                Object.DestroyImmediate(sequenceB);
                Object.DestroyImmediate(stepA);
                Object.DestroyImmediate(stepB);
                Object.DestroyImmediate(tutorial);
                Object.DestroyImmediate(group);
            }
        }

        private static void AssertIssue(
            IEnumerable<GameplayValidationIssue> issues,
            ScriptableObject asset,
            GameplayValidationSeverity severity,
            string message)
        {
            Assert.That(
                issues.Any(issue =>
                    issue.Asset == asset &&
                    issue.Severity == severity &&
                    issue.Message == message),
                Is.True,
                message);
        }
    }
}
