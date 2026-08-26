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

    public sealed class EditorWorkspaceRouteSource
    {
        public EditorWorkspaceRouteSource(
            string moduleId,
            string panelId,
            string subrouteId,
            string displayName)
        {
            ModuleId = RequireId(moduleId, nameof(moduleId));
            PanelId = RequireId(panelId, nameof(panelId));
            SubrouteId = subrouteId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? throw new ArgumentException("Route source display name is required.", nameof(displayName))
                : displayName.Trim();
        }

        public string ModuleId { get; }
        public string PanelId { get; }
        public string SubrouteId { get; }
        public string DisplayName { get; }

        private static string RequireId(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Workspace route IDs are required.", parameterName)
                : value.Trim();
        }
    }

    public sealed class EditorWorkspaceRoute
    {
        public EditorWorkspaceRoute(
            string moduleId,
            string panelId,
            string subrouteId = null,
            EditorWorkspaceRouteSource source = null)
        {
            ModuleId = RequireId(moduleId, nameof(moduleId));
            PanelId = RequireId(panelId, nameof(panelId));
            SubrouteId = subrouteId ?? string.Empty;
            Source = source;
        }

        public string ModuleId { get; }
        public string PanelId { get; }
        public string SubrouteId { get; }
        public EditorWorkspaceRouteSource Source { get; }
        public string FullId => ModuleId + "/" + PanelId;

        private static string RequireId(string value, string parameterName)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Workspace route IDs are required.", parameterName)
                : value.Trim();
        }
    }

    public interface IEditorWorkspaceRouteNavigator
    {
        bool TryShowWorkspace(EditorWorkspaceRoute route);
    }

    public interface IEditorWorkspaceRouteReceiver
    {
        bool TryApplyWorkspaceRoute(string subrouteId);
    }

    public interface IEditorWorkspacePanel : IDisposable
    {
        float RefreshInterval { get; }
        void Activate(EditorWorkspacePanelContext context);
        void Deactivate();
        void Tick(EditorWorkspacePanelContext context, double timeSinceStartup);
        void OnGUI(EditorWorkspacePanelContext context);
    }

    public interface IEditorWorkspaceFullWidthPanel
    {
    }

    public interface IEditorWorkspaceEmbeddedView
    {
        void OnWorkspaceGUI(EditorWorkspacePanelContext context);
    }

    public interface IEditorWorkspaceStatefulView
    {
        string CaptureWorkspaceState();
        void RestoreWorkspaceState(string state);
    }

    public static class EditorWorkspaceNavigation
    {
        public static IEditorToolAction CreateAction(string moduleId, string panelId, string displayName)
        {
            return new DelegateEditorToolAction(context =>
            {
                if (context?.Owner is IEditorWorkspaceNavigator navigator &&
                    navigator.TryShowWorkspace(moduleId, panelId))
                {
                    return new EditorToolActionResult(
                        EditorToolActionStatus.Succeeded,
                        "已切换到工作台：" + displayName + "。");
                }

                return new EditorToolActionResult(
                    EditorToolActionStatus.Failed,
                    "工作台面板当前不可用：" + displayName + "。");
            });
        }

        public static IEditorToolAction CreateRouteAction(EditorWorkspaceRoute route, string displayName)
        {
            if (route == null)
                throw new ArgumentNullException(nameof(route));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Route display name is required.", nameof(displayName));

            return new DelegateEditorToolAction(context =>
            {
                if (context?.Owner is IEditorWorkspaceRouteNavigator navigator &&
                    navigator.TryShowWorkspace(route))
                {
                    return new EditorToolActionResult(
                        EditorToolActionStatus.Succeeded,
                        "已切换到工作台：" + displayName.Trim() + "。");
                }

                return new EditorToolActionResult(
                    EditorToolActionStatus.Failed,
                    "工作台入口当前不可用：" + displayName.Trim() + "。");
            });
        }
    }

    public sealed class EditorWindowWorkspacePanel<TWindow> :
        IEditorWorkspacePanel,
        IEditorWorkspaceFullWidthPanel,
        IEditorWorkspaceRouteReceiver
        where TWindow : EditorWindow, IEditorWorkspaceEmbeddedView
    {
        private const string StateKeyPrefix = "ZeroEngine.EditorUI.WorkspaceWindow.";

        private readonly Func<TWindow> _viewFactory;
        private readonly float _refreshInterval;
        private TWindow _view;
        private string _stateKey = string.Empty;

        public EditorWindowWorkspacePanel(Func<TWindow> viewFactory = null, float refreshInterval = 0f)
        {
            _viewFactory = viewFactory;
            _refreshInterval = Mathf.Max(0f, refreshInterval);
        }

        public float RefreshInterval => _refreshInterval;

        public void Activate(EditorWorkspacePanelContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            DisposeView();
            _stateKey = StateKeyPrefix + context.ModuleId + "." + context.PanelId + "." + typeof(TWindow).FullName;
            _view = _viewFactory != null
                ? _viewFactory()
                : ScriptableObject.CreateInstance<TWindow>();
            if (_view == null)
                throw new InvalidOperationException("Workspace view factory returned null for " + typeof(TWindow).FullName + ".");
            _view.hideFlags = HideFlags.HideAndDontSave;
            if (EditorPrefs.HasKey(_stateKey))
            {
                try
                {
                    string state = EditorPrefs.GetString(_stateKey, string.Empty);
                    if (_view is IEditorWorkspaceStatefulView stateful)
                        stateful.RestoreWorkspaceState(state);
                    else if (!string.IsNullOrEmpty(state))
                        EditorJsonUtility.FromJsonOverwrite(state, _view);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Ignoring invalid workspace state for " + typeof(TWindow).FullName + ": " + exception.Message);
                }
            }
        }

        public void Deactivate()
        {
            DisposeView();
        }

        public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup)
        {
            if (_refreshInterval > 0f)
                context?.RequestRepaint();
        }

        public void OnGUI(EditorWorkspacePanelContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (_view == null)
                Activate(context);

            float width = Mathf.Max(240f, context.AvailableWidth);
            float height = Mathf.Max(420f, context.Owner.position.height - 150f);
            _view.position = new Rect(0f, 0f, width, height);
            _view.OnWorkspaceGUI(context);
        }

        public void Dispose()
        {
            DisposeView();
        }

        public bool TryApplyWorkspaceRoute(string subrouteId)
        {
            if (string.IsNullOrWhiteSpace(subrouteId))
                return true;
            return _view is IEditorWorkspaceRouteReceiver receiver &&
                   receiver.TryApplyWorkspaceRoute(subrouteId.Trim());
        }

        private void DisposeView()
        {
            if (_view == null)
                return;
            if (!string.IsNullOrEmpty(_stateKey))
            {
                try
                {
                    string state = _view is IEditorWorkspaceStatefulView stateful
                        ? stateful.CaptureWorkspaceState()
                        : EditorJsonUtility.ToJson(_view);
                    if (string.IsNullOrEmpty(state) || state == "{}")
                        EditorPrefs.DeleteKey(_stateKey);
                    else
                        EditorPrefs.SetString(_stateKey, state);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Could not save workspace state for " + typeof(TWindow).FullName + ": " + exception.Message);
                }
            }

            UnityEngine.Object.DestroyImmediate(_view);
            _view = null;
        }
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
