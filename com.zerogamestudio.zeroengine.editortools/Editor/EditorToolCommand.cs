using System;

namespace ZeroEngine.EditorTools
{
    public class EditorToolCommand : IEditorToolCommand
    {
        private readonly Func<EditorToolExecutionResult> _execute;

        public EditorToolCommand(
            string id,
            string displayName,
            string group,
            int order,
            Func<EditorToolExecutionResult> execute,
            string tooltip = null,
            string groupDisplayName = null)
        {
            Id = RequireText(id, nameof(id));
            DisplayName = RequireText(displayName, nameof(displayName));
            Tooltip = string.IsNullOrWhiteSpace(tooltip) ? DisplayName : tooltip;
            Group = string.IsNullOrWhiteSpace(group) ? "General" : group;
            GroupDisplayName = string.IsNullOrWhiteSpace(groupDisplayName) ? Group : groupDisplayName;
            Order = order;
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Tooltip { get; }

        public string Group { get; }

        public string GroupDisplayName { get; }

        public int Order { get; }

        public virtual EditorToolExecutionResult Execute()
        {
            return _execute.Invoke() ?? EditorToolExecutionResult.Success();
        }

        public static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty.", parameterName);
            }

            return value;
        }
    }

    public sealed class EditorToolCommandAssetGenerationTask : EditorToolCommand, IAssetGenerationTask
    {
        public EditorToolCommandAssetGenerationTask(
            string id,
            string displayName,
            string group,
            int order,
            Func<EditorToolExecutionResult> execute,
            string tooltip = null,
            string groupDisplayName = null)
            : base(id, displayName, group, order, execute, tooltip, groupDisplayName)
        {
        }
    }

    public sealed class EditorToolCommandValidationTask : EditorToolCommand, IValidationTask
    {
        public EditorToolCommandValidationTask(
            string id,
            string displayName,
            string group,
            int order,
            Func<EditorToolExecutionResult> execute,
            string tooltip = null,
            string groupDisplayName = null)
            : base(id, displayName, group, order, execute, tooltip, groupDisplayName)
        {
        }
    }
}
