using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Interaction;
using ZeroEngine.Tutorial;

namespace ZeroEngine.Gameplay.Editor
{
    public enum GameplayValidationSeverity
    {
        Error,
        Warning,
        Info
    }

    public readonly struct GameplayValidationIssue
    {
        public readonly ScriptableObject Asset;
        public readonly GameplayValidationSeverity Severity;
        public readonly string FieldPath;
        public readonly string Message;

        public GameplayValidationIssue(
            ScriptableObject asset,
            GameplayValidationSeverity severity,
            string fieldPath,
            string message)
        {
            Asset = asset;
            Severity = severity;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    public static class GameplayConfigValidator
    {
        public static IReadOnlyList<T> LoadAssets<T>() where T : ScriptableObject
        {
            var result = new List<T>();
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        public static IReadOnlyList<GameplayValidationIssue> Validate(
            IEnumerable<InteractionConfigSO> interactionConfigs = null,
            IEnumerable<TutorialConfigSO> tutorialConfigs = null,
            IEnumerable<TutorialSequenceSO> tutorialSequences = null,
            IEnumerable<TutorialStepSO> tutorialSteps = null,
            IEnumerable<TutorialSO> tutorials = null,
            IEnumerable<TutorialGroupSO> tutorialGroups = null)
        {
            var issues = new List<GameplayValidationIssue>();
            var interactionConfigList = Materialize(interactionConfigs);
            var tutorialConfigList = Materialize(tutorialConfigs);
            var sequenceList = Materialize(tutorialSequences);
            var stepAssetList = Materialize(tutorialSteps);
            var tutorialList = Materialize(tutorials);
            var groupList = Materialize(tutorialGroups);

            foreach (var config in interactionConfigList)
            {
                ValidateInteractionConfig(config, issues);
            }

            foreach (var config in tutorialConfigList)
            {
                ValidateTutorialConfig(config, issues);
            }

            foreach (var sequence in sequenceList)
            {
                ValidateTutorialSequence(sequence, issues);
            }

            foreach (var step in stepAssetList)
            {
                ValidateTutorialStepAsset(step, issues);
            }

            foreach (var tutorial in tutorialList)
            {
                ValidateTutorial(tutorial, issues);
            }

            foreach (var group in groupList)
            {
                ValidateTutorialGroup(group, issues);
            }

            AddDuplicateStringIssues(sequenceList, sequence => sequence.SequenceId, nameof(TutorialSequenceSO.SequenceId), "Tutorial sequence ID", issues);
            AddDuplicateStringIssues(stepAssetList, step => step.StepId, nameof(TutorialStepSO.StepId), "Tutorial step ID", issues);
            AddDuplicateStringIssues(tutorialList, tutorial => tutorial.TutorialId, nameof(TutorialSO.TutorialId), "Tutorial ID", issues);
            AddDuplicateStringIssues(groupList, group => group.GroupId, nameof(TutorialGroupSO.GroupId), "Tutorial group ID", issues);
            return issues;
        }

        private static T[] Materialize<T>(IEnumerable<T> assets) where T : ScriptableObject
        {
            return (assets ?? Array.Empty<T>())
                .Where(asset => asset != null)
                .ToArray();
        }

        private static void ValidateInteractionConfig(InteractionConfigSO config, ICollection<GameplayValidationIssue> issues)
        {
            RequirePositive(config, issues, nameof(InteractionConfigSO.DefaultDetectionRadius), config.DefaultDetectionRadius, "DefaultDetectionRadius");
            RequirePositive(config, issues, nameof(InteractionConfigSO.DefaultInteractionDistance), config.DefaultInteractionDistance, "DefaultInteractionDistance");
            RequireRange(config, issues, nameof(InteractionConfigSO.DetectionRate), config.DetectionRate, 1, 60, "DetectionRate");
            RequireNonNegative(config, issues, nameof(InteractionConfigSO.OutlineWidth), config.OutlineWidth, "OutlineWidth");
            RequireNonNegative(config, issues, nameof(InteractionConfigSO.PromptShowDelay), config.PromptShowDelay, "PromptShowDelay");
            RequireNonNegative(config, issues, nameof(InteractionConfigSO.PromptFadeInDuration), config.PromptFadeInDuration, "PromptFadeInDuration");
            RequireNonNegative(config, issues, nameof(InteractionConfigSO.PromptFadeOutDuration), config.PromptFadeOutDuration, "PromptFadeOutDuration");

            if (config.DefaultInteractionDistance > config.DefaultDetectionRadius)
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, nameof(InteractionConfigSO.DefaultInteractionDistance), "DefaultInteractionDistance must not be greater than DefaultDetectionRadius."));
            }

            if (config.UseNewInputSystem && string.IsNullOrWhiteSpace(config.InteractActionName))
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, nameof(InteractionConfigSO.InteractActionName), "InteractActionName is empty while UseNewInputSystem is enabled."));
            }

            if (config.PromptUIPrefab == null)
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Warning, nameof(InteractionConfigSO.PromptUIPrefab), "PromptUIPrefab is not assigned."));
            }

            ValidateInteractionHintTemplates(config, issues);
        }

        private static void ValidateInteractionHintTemplates(InteractionConfigSO config, ICollection<GameplayValidationIssue> issues)
        {
            if (config.HintTemplates == null || config.HintTemplates.Count == 0)
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, nameof(InteractionConfigSO.HintTemplates), "HintTemplates are empty."));
                return;
            }

            var seenTypes = new HashSet<InteractionType>();
            for (var i = 0; i < config.HintTemplates.Count; i++)
            {
                var template = config.HintTemplates[i];
                var fieldPath = $"{nameof(InteractionConfigSO.HintTemplates)}[{i}]";
                if (!seenTypes.Add(template.Type))
                {
                    issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(InteractionHintTemplate.Type)}", $"Hint template for {template.Type} is duplicated."));
                }

                if (string.IsNullOrWhiteSpace(template.Template))
                {
                    issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(InteractionHintTemplate.Template)}", "Hint template text is empty."));
                }
                else if (!template.Template.Contains("{0}", StringComparison.Ordinal))
                {
                    issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Warning, $"{fieldPath}.{nameof(InteractionHintTemplate.Template)}", "Hint template should include the '{0}' display-name placeholder."));
                }
            }
        }

        private static void ValidateTutorialConfig(TutorialConfigSO config, ICollection<GameplayValidationIssue> issues)
        {
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.UIFadeInDuration), config.UIFadeInDuration, "UIFadeInDuration");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.UIFadeOutDuration), config.UIFadeOutDuration, "UIFadeOutDuration");
            RequirePositive(config, issues, nameof(TutorialConfigSO.HighlightPulseSpeed), config.HighlightPulseSpeed, "HighlightPulseSpeed");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.HighlightPulseAmount), config.HighlightPulseAmount, "HighlightPulseAmount");
            RequirePositive(config, issues, nameof(TutorialConfigSO.DefaultTypewriterSpeed), config.DefaultTypewriterSpeed, "DefaultTypewriterSpeed");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.DialogueShowDelay), config.DialogueShowDelay, "DialogueShowDelay");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.HighlightBorderWidth), config.HighlightBorderWidth, "HighlightBorderWidth");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.HighlightPadding), config.HighlightPadding, "HighlightPadding");
            RequirePositive(config, issues, nameof(TutorialConfigSO.ArrowSize), config.ArrowSize, "ArrowSize");
            RequirePositive(config, issues, nameof(TutorialConfigSO.ArrowBounceSpeed), config.ArrowBounceSpeed, "ArrowBounceSpeed");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.ArrowBounceAmount), config.ArrowBounceAmount, "ArrowBounceAmount");
            RequireNormalized(config, issues, nameof(TutorialConfigSO.SoundVolume), config.SoundVolume, "SoundVolume");
            RequireNonNegative(config, issues, nameof(TutorialConfigSO.StepTransitionDelay), config.StepTransitionDelay, "StepTransitionDelay");
            RequirePositive(config, issues, nameof(TutorialConfigSO.TargetSearchInterval), config.TargetSearchInterval, "TargetSearchInterval");
            RequirePositive(config, issues, nameof(TutorialConfigSO.MaxTargetSearchAttempts), config.MaxTargetSearchAttempts, "MaxTargetSearchAttempts");

            if (config.EnableTutorials && config.DialogueUIPrefab == null)
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Warning, nameof(TutorialConfigSO.DialogueUIPrefab), "DialogueUIPrefab is not assigned while tutorials are enabled."));
            }

            if (config.DialogueConfirmKey == config.SkipKey)
            {
                issues.Add(new GameplayValidationIssue(config, GameplayValidationSeverity.Error, nameof(TutorialConfigSO.SkipKey), "SkipKey must be different from DialogueConfirmKey."));
            }
        }

        private static void ValidateTutorialSequence(TutorialSequenceSO sequence, ICollection<GameplayValidationIssue> issues)
        {
            RequireId(sequence, issues, nameof(TutorialSequenceSO.SequenceId), sequence.SequenceId, "Tutorial sequence ID");
            RequireDisplayName(sequence, issues, nameof(TutorialSequenceSO.DisplayName), sequence.DisplayName, "Tutorial sequence display name");
            RequireDisplayName(sequence, issues, nameof(TutorialSequenceSO.Description), sequence.Description, "Tutorial sequence description");

            ValidateTutorialConditions(sequence, sequence.StartConditions, nameof(TutorialSequenceSO.StartConditions), issues);
            ValidateTutorialSteps(sequence, sequence.Steps, nameof(TutorialSequenceSO.Steps), issues);
            ValidateTutorialRewards(sequence, sequence.CompletionRewards, nameof(TutorialSequenceSO.CompletionRewards), issues);
            ValidateIdList(sequence, sequence.Prerequisites, nameof(TutorialSequenceSO.Prerequisites), "Prerequisite tutorial ID", sequence.SequenceId, issues);
            ValidateIdList(sequence, sequence.MutuallyExclusive, nameof(TutorialSequenceSO.MutuallyExclusive), "Mutually exclusive tutorial ID", sequence.SequenceId, issues);

            if (!string.IsNullOrWhiteSpace(sequence.NextSequenceId) &&
                string.Equals(sequence.SequenceId?.Trim(), sequence.NextSequenceId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new GameplayValidationIssue(sequence, GameplayValidationSeverity.Error, nameof(TutorialSequenceSO.NextSequenceId), "NextSequenceId must not reference the same sequence."));
            }
        }

        private static void ValidateTutorialStepAsset(TutorialStepSO step, ICollection<GameplayValidationIssue> issues)
        {
            RequireId(step, issues, nameof(TutorialStepSO.StepId), step.StepId, "Tutorial step ID");
            RequireDisplayName(step, issues, nameof(TutorialStepSO.Title), step.Title, "Tutorial step title");
            RequireDisplayName(step, issues, nameof(TutorialStepSO.Description), step.Description, "Tutorial step description");
            RequireNonNegative(step, issues, nameof(TutorialStepSO.TriggerDelay), step.TriggerDelay, "TriggerDelay");
            RequireNonNegative(step, issues, nameof(TutorialStepSO.AutoCompleteDelay), step.AutoCompleteDelay, "AutoCompleteDelay");

            if (step.TriggerType == TriggerType.OnKeyPress && step.TriggerKey == KeyCode.None)
            {
                issues.Add(new GameplayValidationIssue(step, GameplayValidationSeverity.Error, nameof(TutorialStepSO.TriggerKey), "TriggerKey is None for OnKeyPress trigger."));
            }

            if (step.TriggerType == TriggerType.OnEvent && string.IsNullOrWhiteSpace(step.TriggerEventId))
            {
                issues.Add(new GameplayValidationIssue(step, GameplayValidationSeverity.Error, nameof(TutorialStepSO.TriggerEventId), "TriggerEventId is empty for OnEvent trigger."));
            }

            if (step.TriggerType == TriggerType.OnDelay)
            {
                RequirePositive(step, issues, nameof(TutorialStepSO.TriggerDelay), step.TriggerDelay, "TriggerDelay");
            }

            ValidateStepConditions(step, step.CompleteConditions, nameof(TutorialStepSO.CompleteConditions), issues);
            ValidateHighlights(step, step.Highlights, nameof(TutorialStepSO.Highlights), issues);
            ValidateTooltip(step, step.Tooltip, nameof(TutorialStepSO.Tooltip), issues);
            ValidateStepActions(step, step.OnStartActions, nameof(TutorialStepSO.OnStartActions), issues);
            ValidateStepActions(step, step.OnCompleteActions, nameof(TutorialStepSO.OnCompleteActions), issues);
        }

        private static void ValidateTutorial(TutorialSO tutorial, ICollection<GameplayValidationIssue> issues)
        {
            RequireId(tutorial, issues, nameof(TutorialSO.TutorialId), tutorial.TutorialId, "Tutorial ID");
            RequireDisplayName(tutorial, issues, nameof(TutorialSO.DisplayName), tutorial.DisplayName, "Tutorial display name");
            RequireDisplayName(tutorial, issues, nameof(TutorialSO.Description), tutorial.Description, "Tutorial description");

            if (tutorial.Steps == null || tutorial.Steps.Count == 0)
            {
                issues.Add(new GameplayValidationIssue(tutorial, GameplayValidationSeverity.Error, nameof(TutorialSO.Steps), "Tutorial has no step assets."));
            }
            else
            {
                var seenSteps = new HashSet<TutorialStepSO>();
                for (var i = 0; i < tutorial.Steps.Count; i++)
                {
                    var step = tutorial.Steps[i];
                    var fieldPath = $"{nameof(TutorialSO.Steps)}[{i}]";
                    if (step == null)
                    {
                        issues.Add(new GameplayValidationIssue(tutorial, GameplayValidationSeverity.Error, fieldPath, "Tutorial step asset is missing."));
                    }
                    else if (!seenSteps.Add(step))
                    {
                        issues.Add(new GameplayValidationIssue(tutorial, GameplayValidationSeverity.Error, fieldPath, "Tutorial contains a duplicate step asset reference."));
                    }
                }
            }

            if (tutorial.Prerequisites != null)
            {
                for (var i = 0; i < tutorial.Prerequisites.Count; i++)
                {
                    var prerequisite = tutorial.Prerequisites[i];
                    var fieldPath = $"{nameof(TutorialSO.Prerequisites)}[{i}]";
                    if (prerequisite == null)
                    {
                        issues.Add(new GameplayValidationIssue(tutorial, GameplayValidationSeverity.Warning, fieldPath, "Tutorial prerequisite is missing."));
                    }
                    else if (prerequisite == tutorial)
                    {
                        issues.Add(new GameplayValidationIssue(tutorial, GameplayValidationSeverity.Error, fieldPath, "Tutorial must not require itself as a prerequisite."));
                    }
                }
            }
        }

        private static void ValidateTutorialGroup(TutorialGroupSO group, ICollection<GameplayValidationIssue> issues)
        {
            RequireId(group, issues, nameof(TutorialGroupSO.GroupId), group.GroupId, "Tutorial group ID");
            RequireDisplayName(group, issues, nameof(TutorialGroupSO.DisplayName), group.DisplayName, "Tutorial group display name");
            RequireDisplayName(group, issues, nameof(TutorialGroupSO.Description), group.Description, "Tutorial group description");

            if (group.Tutorials == null || group.Tutorials.Count == 0)
            {
                issues.Add(new GameplayValidationIssue(group, GameplayValidationSeverity.Warning, nameof(TutorialGroupSO.Tutorials), "Tutorial group has no tutorials."));
                return;
            }

            var seenTutorials = new HashSet<TutorialSO>();
            for (var i = 0; i < group.Tutorials.Count; i++)
            {
                var tutorial = group.Tutorials[i];
                var fieldPath = $"{nameof(TutorialGroupSO.Tutorials)}[{i}]";
                if (tutorial == null)
                {
                    issues.Add(new GameplayValidationIssue(group, GameplayValidationSeverity.Error, fieldPath, "Tutorial group entry is missing."));
                }
                else if (!seenTutorials.Add(tutorial))
                {
                    issues.Add(new GameplayValidationIssue(group, GameplayValidationSeverity.Error, fieldPath, "Tutorial group contains a duplicate tutorial reference."));
                }
            }
        }

        private static void ValidateTutorialSteps(
            ScriptableObject asset,
            IReadOnlyList<TutorialStep> steps,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (steps == null || steps.Count == 0)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, listPath, "Tutorial sequence has no steps."));
                return;
            }

            var seenStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var fieldPath = $"{listPath}[{i}]";
                if (step == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Tutorial sequence step is empty."));
                    continue;
                }

                ValidateTutorialStep(asset, step, fieldPath, issues);
                if (!string.IsNullOrWhiteSpace(step.StepId) && !seenStepIds.Add(step.StepId.Trim()))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(TutorialStep.StepId)}", $"Tutorial step ID '{step.StepId.Trim()}' is duplicated inside the sequence."));
                }
            }
        }

        private static void ValidateTutorialStep(
            ScriptableObject asset,
            TutorialStep step,
            string fieldPath,
            ICollection<GameplayValidationIssue> issues)
        {
            RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(TutorialStep.AutoCompleteDelay)}", step.AutoCompleteDelay, "AutoCompleteDelay");

            switch (step)
            {
                case DialogueStep dialogue:
                    if (string.IsNullOrWhiteSpace(dialogue.DialogueText))
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(DialogueStep.DialogueText)}", "DialogueText is empty."));
                    }

                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(DialogueStep.TypewriterSpeed)}", dialogue.TypewriterSpeed, "TypewriterSpeed");
                    if (dialogue.WaitForConfirm && dialogue.ConfirmKey == KeyCode.None)
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(DialogueStep.ConfirmKey)}", "ConfirmKey is None while WaitForConfirm is enabled."));
                    }
                    break;
                case HighlightStep highlight:
                    if (string.IsNullOrWhiteSpace(highlight.TargetPath))
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(HighlightStep.TargetPath)}", "Highlight TargetPath is empty."));
                    }

                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(HighlightStep.Timeout)}", highlight.Timeout, "Highlight Timeout");
                    break;
                case DelayStep delay:
                    RequirePositive(asset, issues, $"{fieldPath}.{nameof(DelayStep.Duration)}", delay.Duration, "Delay Duration");
                    break;
                case CallbackStep callback:
                    if (string.IsNullOrWhiteSpace(callback.CallbackId))
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(CallbackStep.CallbackId)}", "CallbackId is empty."));
                    }
                    break;
                case WaitInputStep input:
                    if (input.RequiredKeys == null || input.RequiredKeys.Length == 0)
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(WaitInputStep.RequiredKeys)}", "WaitInput step has no required keys."));
                    }

                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(WaitInputStep.Timeout)}", input.Timeout, "WaitInput Timeout");
                    break;
                case WaitEventStep waitEvent:
                    if (string.IsNullOrWhiteSpace(waitEvent.EventKey))
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(WaitEventStep.EventKey)}", "WaitEvent EventKey is empty."));
                    }

                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(WaitEventStep.Timeout)}", waitEvent.Timeout, "WaitEvent Timeout");
                    break;
                case WaitInteractionStep interaction:
                    if (string.IsNullOrWhiteSpace(interaction.InteractableId) && interaction.InteractionRequirement == InteractionRequirement.Any)
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(WaitInteractionStep.InteractableId)}", "WaitInteraction needs InteractableId or a specific InteractionRequirement."));
                    }

                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(WaitInteractionStep.Timeout)}", interaction.Timeout, "WaitInteraction Timeout");
                    break;
                case MoveToStep moveTo:
                    if (moveTo.TargetPosition == Vector3.zero && string.IsNullOrWhiteSpace(moveTo.TargetObjectPath))
                    {
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(MoveToStep.TargetObjectPath)}", "MoveTo needs TargetPosition or TargetObjectPath."));
                    }

                    RequirePositive(asset, issues, $"{fieldPath}.{nameof(MoveToStep.ArrivalDistance)}", moveTo.ArrivalDistance, "ArrivalDistance");
                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(MoveToStep.Timeout)}", moveTo.Timeout, "MoveTo Timeout");
                    RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(MoveToStep.ArrivalDelay)}", moveTo.ArrivalDelay, "ArrivalDelay");
                    break;
                case CompositeStep composite:
                    ValidateTutorialSteps(asset, composite.SubSteps, $"{fieldPath}.{nameof(CompositeStep.SubSteps)}", issues);
                    break;
            }
        }

        private static void ValidateTutorialConditions(
            ScriptableObject asset,
            IReadOnlyList<TutorialCondition> conditions,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (conditions == null)
            {
                return;
            }

            for (var i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                var fieldPath = $"{listPath}[{i}]";
                if (condition == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Tutorial condition is empty."));
                    continue;
                }

                switch (condition)
                {
                    case FirstTimeCondition firstTime when string.IsNullOrWhiteSpace(firstTime.Key):
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Warning, $"{fieldPath}.{nameof(FirstTimeCondition.Key)}", "FirstTime condition Key is empty."));
                        break;
                    case LevelCondition level:
                        RequirePositive(asset, issues, $"{fieldPath}.{nameof(LevelCondition.MinLevel)}", level.MinLevel, "Level condition MinLevel");
                        RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(LevelCondition.MaxLevel)}", level.MaxLevel, "Level condition MaxLevel");
                        if (level.MaxLevel > 0 && level.MaxLevel < level.MinLevel)
                        {
                            issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(LevelCondition.MaxLevel)}", "Level condition MaxLevel is lower than MinLevel."));
                        }
                        break;
                    case TutorialCompletedCondition completed:
                        ValidateStringArray(asset, completed.RequiredTutorialIds, $"{fieldPath}.{nameof(TutorialCompletedCondition.RequiredTutorialIds)}", "Required tutorial ID", issues);
                        break;
                    case SceneCondition scene:
                        ValidateStringArray(asset, scene.AllowedScenes, $"{fieldPath}.{nameof(SceneCondition.AllowedScenes)}", "Allowed scene", issues);
                        ValidateStringArray(asset, scene.ExcludedScenes, $"{fieldPath}.{nameof(SceneCondition.ExcludedScenes)}", "Excluded scene", issues);
                        break;
                    case QuestCondition quest when string.IsNullOrWhiteSpace(quest.QuestId):
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(QuestCondition.QuestId)}", "Quest condition QuestId is empty."));
                        break;
                    case VariableCondition variable when string.IsNullOrWhiteSpace(variable.VariableKey):
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(VariableCondition.VariableKey)}", "Variable condition key is empty."));
                        break;
                }
            }
        }

        private static void ValidateStepConditions(
            ScriptableObject asset,
            IReadOnlyList<StepCondition> conditions,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (conditions == null)
            {
                return;
            }

            for (var i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                var fieldPath = $"{listPath}[{i}]";
                if (condition == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Step condition is empty."));
                    continue;
                }

                if (condition.Type != ConditionType.None && string.IsNullOrWhiteSpace(condition.TargetId))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(StepCondition.TargetId)}", "Step condition TargetId is empty."));
                }

                RequireNonNegative(asset, issues, $"{fieldPath}.{nameof(StepCondition.TargetValue)}", condition.TargetValue, "Step condition TargetValue");
            }
        }

        private static void ValidateHighlights(
            ScriptableObject asset,
            IReadOnlyList<HighlightTarget> highlights,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (highlights == null)
            {
                return;
            }

            for (var i = 0; i < highlights.Count; i++)
            {
                var highlight = highlights[i];
                var fieldPath = $"{listPath}[{i}]";
                if (highlight == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Highlight target is empty."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(highlight.TargetPath))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(HighlightTarget.TargetPath)}", "Highlight TargetPath is empty."));
                }

                RequirePositive(asset, issues, $"{fieldPath}.{nameof(HighlightTarget.Scale)}", highlight.Scale, "Highlight scale");
            }
        }

        private static void ValidateTooltip(
            ScriptableObject asset,
            TooltipConfig tooltip,
            string fieldPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (tooltip == null)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Tooltip config is missing."));
                return;
            }

            if (tooltip.Show && string.IsNullOrWhiteSpace(tooltip.Text))
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Warning, $"{fieldPath}.{nameof(TooltipConfig.Text)}", "Tooltip text is empty while tooltip is shown."));
            }
        }

        private static void ValidateStepActions(
            ScriptableObject asset,
            IReadOnlyList<StepAction> actions,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (actions == null)
            {
                return;
            }

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                var fieldPath = $"{listPath}[{i}]";
                if (action == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Step action is empty."));
                    continue;
                }

                if (action.Type != ActionType.None && string.IsNullOrWhiteSpace(action.ActionId))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(StepAction.ActionId)}", "Step action ActionId is empty."));
                }
            }
        }

        private static void ValidateTutorialRewards(
            ScriptableObject asset,
            IReadOnlyList<TutorialReward> rewards,
            string listPath,
            ICollection<GameplayValidationIssue> issues)
        {
            if (rewards == null)
            {
                return;
            }

            for (var i = 0; i < rewards.Count; i++)
            {
                var reward = rewards[i];
                var fieldPath = $"{listPath}[{i}]";
                if (reward == null)
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, "Tutorial reward is empty."));
                    continue;
                }

                switch (reward)
                {
                    case ItemTutorialReward item:
                        if (string.IsNullOrWhiteSpace(item.ItemId))
                        {
                            issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(ItemTutorialReward.ItemId)}", "Item reward ItemId is empty."));
                        }

                        RequirePositive(asset, issues, $"{fieldPath}.{nameof(ItemTutorialReward.Amount)}", item.Amount, "Item reward Amount");
                        break;
                    case AchievementTutorialReward achievement when string.IsNullOrWhiteSpace(achievement.AchievementId):
                        issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}.{nameof(AchievementTutorialReward.AchievementId)}", "Achievement reward AchievementId is empty."));
                        break;
                }
            }
        }

        private static void ValidateIdList(
            ScriptableObject asset,
            IReadOnlyList<string> ids,
            string fieldPath,
            string label,
            string selfId,
            ICollection<GameplayValidationIssue> issues)
        {
            if (ids == null)
            {
                return;
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < ids.Count; i++)
            {
                var value = ids[i];
                var itemPath = $"{fieldPath}[{i}]";
                if (string.IsNullOrWhiteSpace(value))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, itemPath, $"{label} is empty."));
                    continue;
                }

                var trimmed = value.Trim();
                if (!seenIds.Add(trimmed))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, itemPath, $"{label} '{trimmed}' is duplicated."));
                }

                if (!string.IsNullOrWhiteSpace(selfId) && string.Equals(trimmed, selfId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, itemPath, $"{label} must not reference the owning sequence."));
                }
            }
        }

        private static void ValidateStringArray(
            ScriptableObject asset,
            IReadOnlyList<string> values,
            string fieldPath,
            string label,
            ICollection<GameplayValidationIssue> issues)
        {
            if (values == null)
            {
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, $"{fieldPath}[{i}]", $"{label} is empty."));
                }
            }
        }

        private static void RequireId(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} is empty."));
            }
            else if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Warning, fieldPath, $"{label} has leading/trailing whitespace."));
            }
        }

        private static void RequireDisplayName(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            string value,
            string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Warning, fieldPath, $"{label} is empty."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value <= 0)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequirePositive(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value <= 0f)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must be greater than 0."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            int value,
            string label)
        {
            if (value < 0)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNonNegative(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must not be negative."));
            }
        }

        private static void RequireNormalized(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            float value,
            string label)
        {
            if (value < 0f || value > 1f)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must be between 0 and 1."));
            }
        }

        private static void RequireRange(
            ScriptableObject asset,
            ICollection<GameplayValidationIssue> issues,
            string fieldPath,
            int value,
            int minInclusive,
            int maxInclusive,
            string label)
        {
            if (value < minInclusive || value > maxInclusive)
            {
                issues.Add(new GameplayValidationIssue(asset, GameplayValidationSeverity.Error, fieldPath, $"{label} must be between {minInclusive} and {maxInclusive}."));
            }
        }

        private static void AddDuplicateStringIssues<T>(
            IEnumerable<T> assets,
            Func<T, string> keySelector,
            string fieldPath,
            string label,
            ICollection<GameplayValidationIssue> issues)
            where T : ScriptableObject
        {
            foreach (var duplicateGroup in assets
                         .Select(asset => new { Asset = asset, Key = keySelector(asset)?.Trim() })
                         .Where(record => !string.IsNullOrEmpty(record.Key))
                         .GroupBy(record => record.Key, StringComparer.OrdinalIgnoreCase))
            {
                var duplicates = duplicateGroup.ToArray();
                if (duplicates.Length <= 1)
                {
                    continue;
                }

                foreach (var duplicate in duplicates)
                {
                    issues.Add(new GameplayValidationIssue(
                        duplicate.Asset,
                        GameplayValidationSeverity.Error,
                        fieldPath,
                        $"{label} '{duplicate.Key}' is duplicated in {duplicates.Length} assets."));
                }
            }
        }
    }
}
