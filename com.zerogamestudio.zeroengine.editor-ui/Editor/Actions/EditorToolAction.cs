using System;
using UnityEditor;

namespace ZeroEngine.EditorUI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EditorToolActionProviderAttribute : Attribute
    {
        public EditorToolActionProviderAttribute(string providerId)
        {
            ProviderId = providerId ?? string.Empty;
        }

        public string ProviderId { get; }
    }

    public interface IEditorToolActionProvider
    {
        IEditorToolAction CreateAction(string actionId);
    }

    public interface IEditorToolAction
    {
        EditorToolActionState GetState();
        EditorToolActionResult Execute(EditorToolActionContext context);
    }

    public sealed class DelegateEditorToolAction : IEditorToolAction
    {
        private readonly Func<EditorToolActionState> _getState;
        private readonly Func<EditorToolActionContext, EditorToolActionResult> _execute;

        public DelegateEditorToolAction(
            Func<EditorToolActionContext, EditorToolActionResult> execute,
            Func<EditorToolActionState> getState = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _getState = getState ?? (() => new EditorToolActionState(true));
        }

        public EditorToolActionState GetState()
        {
            return _getState();
        }

        public EditorToolActionResult Execute(EditorToolActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            return _execute(context);
        }
    }

    public sealed class EditorToolActionState
    {
        public EditorToolActionState(bool enabled, bool isChecked = false, string disabledReason = null)
        {
            if (!enabled && string.IsNullOrWhiteSpace(disabledReason))
            {
                throw new ArgumentException(
                    "Disabled actions must provide a user-facing reason.",
                    nameof(disabledReason));
            }

            Enabled = enabled;
            IsChecked = isChecked;
            DisabledReason = disabledReason ?? string.Empty;
        }

        public bool Enabled { get; }
        public bool IsChecked { get; }
        public string DisabledReason { get; }
    }

    public enum EditorToolActionStatus
    {
        Succeeded,
        Cancelled,
        Failed
    }

    public sealed class EditorToolActionResult
    {
        public EditorToolActionResult(EditorToolActionStatus status, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Action results must provide a user-facing summary.", nameof(message));

            Status = status;
            Message = message;
        }

        public EditorToolActionStatus Status { get; }
        public string Message { get; }
    }

    public sealed class EditorToolActionContext
    {
        public EditorToolActionContext(EditorWindow owner, string moduleId, string entryId)
        {
            Owner = owner ? owner : throw new ArgumentNullException(nameof(owner));
            ModuleId = string.IsNullOrWhiteSpace(moduleId)
                ? throw new ArgumentException("Module ID is required.", nameof(moduleId))
                : moduleId;
            EntryId = string.IsNullOrWhiteSpace(entryId)
                ? throw new ArgumentException("Entry ID is required.", nameof(entryId))
                : entryId;
        }

        public EditorWindow Owner { get; }
        public string ModuleId { get; }
        public string EntryId { get; }
        public string FullId => ModuleId + "/" + EntryId;

        public void RequestRepaint()
        {
            Owner.Repaint();
        }
    }
}
