using System;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.EditorUI
{
    public enum EditorWorkspaceActionSafety
    {
        Navigation,
        ReadOnly,
        ProjectWrite,
        Destructive
    }

    public enum EditorWorkspaceActionStyle
    {
        Primary,
        Secondary,
        Destructive
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EditorWorkspacePanelProviderAttribute : Attribute
    {
        public EditorWorkspacePanelProviderAttribute(string providerId)
        {
            ProviderId = providerId ?? string.Empty;
        }

        public string ProviderId { get; }
    }

    public interface IEditorWorkspacePanelProvider
    {
        IEditorWorkspacePanel CreatePanel(string panelId);
    }

    public interface IEditorWorkspaceNavigator
    {
        bool TryShowWorkspace(string moduleId, string panelId);
    }

    public interface IEditorWorkspacePanel : IDisposable
    {
        float RefreshInterval { get; }
        void Activate(EditorWorkspacePanelContext context);
        void Deactivate();
        void Tick(EditorWorkspacePanelContext context, double timeSinceStartup);
        void OnGUI(EditorWorkspacePanelContext context);
    }

    public sealed class EditorWorkspaceAction
    {
        public EditorWorkspaceAction(
            GUIContent content,
            Action execute,
            EditorWorkspaceActionSafety safety = EditorWorkspaceActionSafety.Navigation,
            EditorWorkspaceActionStyle style = EditorWorkspaceActionStyle.Secondary,
            string confirmation = null,
            bool enabled = true)
        {
            Content = content ?? GUIContent.none;
            Execute = execute ?? throw new ArgumentNullException(nameof(execute));
            Safety = safety;
            Style = style;
            Confirmation = confirmation ?? string.Empty;
            Enabled = enabled;
        }

        public GUIContent Content { get; }
        public Action Execute { get; }
        public EditorWorkspaceActionSafety Safety { get; }
        public EditorWorkspaceActionStyle Style { get; }
        public string Confirmation { get; }
        public bool Enabled { get; }
    }

    public sealed class EditorWorkspacePanelContext
    {
        private readonly Func<EditorWorkspaceAction, GUILayoutOption[], bool> _drawAction;

        public EditorWorkspacePanelContext(
            EditorWindow owner,
            string moduleId,
            string panelId,
            Func<EditorWorkspaceAction, GUILayoutOption[], bool> drawAction)
        {
            Owner = owner ? owner : throw new ArgumentNullException(nameof(owner));
            ModuleId = moduleId ?? string.Empty;
            PanelId = panelId ?? string.Empty;
            _drawAction = drawAction ?? throw new ArgumentNullException(nameof(drawAction));
        }

        public EditorWindow Owner { get; }
        public string ModuleId { get; }
        public string PanelId { get; }
        public float AvailableWidth { get; set; }

        public bool DrawAction(EditorWorkspaceAction action, params GUILayoutOption[] options)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return _drawAction(action, options ?? Array.Empty<GUILayoutOption>());
        }

        public void RequestRepaint()
        {
            Owner.Repaint();
        }
    }
}
