using System;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public enum WorldGraphWorkbenchActionKind
    {
        RunValidation,
        RunGraduation,
        OpenAsset,
        OpenScene,
        ProjectCommand
    }

    public enum WorldGraphWorkbenchActionRisk
    {
        None,
        OpensScene,
        WritesAssets
    }

    public readonly struct WorldGraphWorkbenchActionDescriptor
    {
        public WorldGraphWorkbenchActionDescriptor(
            string actionId,
            string label,
            string description,
            WorldGraphWorkbenchActionKind kind,
            WorldGraphWorkbenchActionRisk risk,
            string targetPath,
            bool requiresConfirmation)
        {
            ActionId = actionId ?? string.Empty;
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
            Kind = kind;
            Risk = risk;
            TargetPath = targetPath ?? string.Empty;
            RequiresConfirmation = requiresConfirmation;
        }

        public string ActionId { get; }
        public string Label { get; }
        public string Description { get; }
        public WorldGraphWorkbenchActionKind Kind { get; }
        public WorldGraphWorkbenchActionRisk Risk { get; }
        public string TargetPath { get; }
        public bool RequiresConfirmation { get; }
    }

    public sealed class WorldGraphWorkbenchAction
    {
        private readonly Action _execute;

        private WorldGraphWorkbenchAction(
            string actionId,
            string label,
            string description,
            WorldGraphWorkbenchActionRisk risk,
            bool requiresConfirmation,
            Action execute)
        {
            ActionId = string.IsNullOrWhiteSpace(actionId)
                ? throw new ArgumentException("Action id must not be empty.", nameof(actionId))
                : actionId.Trim();
            Label = string.IsNullOrWhiteSpace(label)
                ? throw new ArgumentException("Action label must not be empty.", nameof(label))
                : label.Trim();
            Description = description ?? string.Empty;
            Risk = risk;
            RequiresConfirmation = requiresConfirmation;
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public string ActionId { get; }
        public string Label { get; }
        public string Description { get; }
        public WorldGraphWorkbenchActionRisk Risk { get; }
        public bool RequiresConfirmation { get; }

        public static WorldGraphWorkbenchAction CreateProjectCommand(
            string actionId,
            string label,
            string description,
            WorldGraphWorkbenchActionRisk risk,
            bool requiresConfirmation,
            Action execute)
        {
            return new WorldGraphWorkbenchAction(
                actionId,
                label,
                description,
                risk,
                requiresConfirmation,
                execute);
        }

        public WorldGraphWorkbenchActionDescriptor ToDescriptor()
        {
            return new WorldGraphWorkbenchActionDescriptor(
                ActionId,
                Label,
                Description,
                WorldGraphWorkbenchActionKind.ProjectCommand,
                Risk,
                string.Empty,
                RequiresConfirmation);
        }

        public bool Execute(out string error)
        {
            try
            {
                _execute();
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
}
