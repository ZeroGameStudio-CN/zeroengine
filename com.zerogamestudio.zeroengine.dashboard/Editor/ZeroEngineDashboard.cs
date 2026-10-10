using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using ZeroEngine.Editor.Dashboard;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Editor
{
    [EditorUiSurface]
    public sealed class ZeroEngineDashboard : EditorWindow, IEditorWorkspaceNavigator
    {
        private const string WorkspaceModuleDragDataKey = "ZGS.Workbench.WorkspaceModule";
        private const string WorkspacePanelDragDataKey = "ZGS.Workbench.WorkspacePanel";
        private const float WorkspaceSidebarMinWidth = 148f;
        private const float WorkspaceSidebarMaxWidth = 360f;
        private const float WorkspaceSidebarMaximumFraction = 0.42f;
        private const float WorkspaceSidebarSplitterWidth = 6f;
        private const float StackedNavigationMaxWidth = 560f;
        private const float DashboardPageHorizontalInset = 12f;
        private const float WorkspaceNavigationRightInset = 8f;
        private const float WorkspacePanelHandleInset = 12f;
        private const float WorkspacePanelHandleWidth = 14f;
        private const float WorkspacePanelGap = 2f;
        private const float WorkspacePanelVerticalInset = 4f;
        private const float WorkspaceSelectionBarWidth = 3f;
        private const int HomeViewOverview = 0;
        private const int HomeViewAllTools = 1;

        private static readonly GUIContent[] PageNames =
        {
            new GUIContent(DashboardText.Home, DashboardText.HomeTooltip),
            new GUIContent(DashboardText.System, DashboardText.SystemTooltip),
            new GUIContent(DashboardText.Help, DashboardText.HelpTooltip)
        };

        private static readonly string[] ToolCategoryIds =
        {
            "authoring",
            "data-localization",
            "assets-build",
            "diagnostics",
            "test-release",
            "system-setup"
        };

        private static readonly GUIContent[] ToolCategoryNames =
        {
            new GUIContent(DashboardText.CategoryAuthoring, DashboardText.CategoryAuthoringTooltip),
            new GUIContent(DashboardText.CategoryData, DashboardText.CategoryDataTooltip),
            new GUIContent(DashboardText.CategoryAssets, DashboardText.CategoryAssetsTooltip),
            new GUIContent(DashboardText.CategoryDiagnostics, DashboardText.CategoryDiagnosticsTooltip),
            new GUIContent(DashboardText.CategoryRelease, DashboardText.CategoryReleaseTooltip),
            new GUIContent(DashboardText.CategorySystem, DashboardText.CategorySystemTooltip)
        };

        private readonly Dictionary<string, DashboardDiagnostic> _runtimeDiagnostics =
            new Dictionary<string, DashboardDiagnostic>(StringComparer.Ordinal);
        private readonly List<string> _workspaceModuleOrder = new List<string>();
        private readonly List<string> _workspacePanelOrder = new List<string>();
        private readonly HashSet<string> _collapsedWorkspaceModules = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkspacePanelView> _workspacePanelsById =
            new Dictionary<string, WorkspacePanelView>(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkspacePanelView[]> _workspacePanelsByModule =
            new Dictionary<string, WorkspacePanelView[]>(StringComparer.Ordinal);
        private DashboardModule[] _orderedWorkspaceModules = Array.Empty<DashboardModule>();
        private WorkspacePanelView[] _orderedWorkspacePanels = Array.Empty<WorkspacePanelView>();
        private DashboardModule[] _visibleWorkspaceModules = Array.Empty<DashboardModule>();
        private WorkspaceModuleView[] _visibleWorkspaceModuleViews = Array.Empty<WorkspaceModuleView>();
        private WorkspacePanelView[] _visibleWorkspacePanels = Array.Empty<WorkspacePanelView>();
        private InstalledPackageView[] _installedPackageViews = Array.Empty<InstalledPackageView>();
        private DashboardCatalog _workspaceViewCatalog;
        private DashboardWorkspaceRegistry _workspaceViewRegistry;
        private string _workspaceViewSearch;
        private bool _workspaceViewDirty = true;

        private DashboardCatalog _catalog = DashboardCatalog.Empty;
        private int _page;
        private int _homeView;
        private string _search = string.Empty;
        private string _selectedCategoryId = "authoring";
        private string _selectedScopeId = string.Empty;
        private string _selectedSafetyId = string.Empty;
        private string _selectedAvailabilityId = string.Empty;
        private bool _showAdvanced = true;
        private bool _showMaintenance;
        private bool _focusSearch;
        private Vector2 _moduleScroll;
        private Vector2 _contentScroll;
        private Vector2 _systemScroll;
        private Vector2 _workspaceNavigationScroll;
        private Vector2 _workspaceContentScroll;
        private Vector2 _contextScroll;
        private float _workspaceSidebarWidth = DashboardViewState.DefaultWorkspaceSidebarWidth;
        private bool _showInstalledPackages = true;
        private bool _showConnectedPackages = true;
        private bool _showPackageIssues = true;
        private bool _showPackagesWithoutWorkspaceEntry;
        private bool _showProjectAdapters;
        private bool _showContext;
        private bool _showDeveloperInfo;
        private string _selectedPanelFullId = string.Empty;
        private DashboardModule _helpModule;
        private DashboardSurface _helpSurface;
        private DashboardPanel _helpPanel;
        private DashboardWorkspaceRegistry _workspaceRegistry;
        private DashboardActionRegistry _actionRegistry;
        private DashboardPanel _activePanelDescriptor;
        private IEditorWorkspacePanel _activePanel;
        private EditorWorkspacePanelContext _activePanelContext;
        private double _nextPanelTick;
        private bool _catalogLoading;
        private bool _catalogRefreshQueued;
        private bool _hasDrawnShell;
        private bool _deferRestoredPanelActivation;
        private string _pressedWorkspaceModuleId = string.Empty;
        private string _pressedWorkspacePanelId = string.Empty;
        private string _workspaceModuleDropTargetId = string.Empty;
        private bool _workspaceModuleDropBefore;
        private string _workspaceDropTargetId = string.Empty;
        private bool _workspaceDropBefore;
        private readonly HashSet<string> _failedWorkspacePanels = new HashSet<string>(StringComparer.Ordinal);

        [MenuItem("ZGS/工作台")]
        public static void ShowWindow()
        {
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>(DashboardText.WindowTitle);
            window.titleContent = new GUIContent(DashboardText.WindowTitle, DashboardText.HomeTooltip);
            window.minSize = new Vector2(980f, 560f);
            window._focusSearch = true;
            window.Show();
            window.Focus();
        }

        public static void ShowWorkspace(string moduleId, string panelId)
        {
            ShowWindow();
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>(DashboardText.WindowTitle);
            window.TryShowWorkspaceInternal(moduleId, panelId);
            window.Show();
            window.Focus();
            window.Repaint();
        }

        private void OnEnable()
        {
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged += InvalidateAndQueueCatalogRefresh;
            EditorApplication.update += OnEditorUpdate;
            minSize = new Vector2(980f, 560f);
            RestoreViewState();
            _hasDrawnShell = false;
            _deferRestoredPanelActivation = !string.IsNullOrEmpty(_selectedPanelFullId);
            if (DashboardCatalogSession.TryGet(out DashboardCatalog cachedCatalog))
                ApplyCatalog(cachedCatalog);
            else
                QueueCatalogRefresh();
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged -= InvalidateAndQueueCatalogRefresh;
            EditorApplication.update -= OnEditorUpdate;
            _catalogRefreshQueued = false;
            SaveViewState();
            DeactivateWorkspacePanel();
            _actionRegistry = null;
        }

        private void OnRegisteredPackages(PackageRegistrationEventArgs eventArgs)
        {
            InvalidateAndQueueCatalogRefresh();
        }

        private void InvalidateAndQueueCatalogRefresh()
        {
            DashboardCatalogSession.Invalidate();
            QueueCatalogRefresh();
        }

        private void QueueCatalogRefresh()
        {
            if (_catalogRefreshQueued)
                return;
            _catalogLoading = true;
            _catalogRefreshQueued = true;
            Repaint();
        }

        private void RefreshCatalogNow()
        {
            _catalogRefreshQueued = false;
            DashboardCatalog catalog;
            try
            {
                catalog = DashboardCatalogDiscovery.Discover();
                DashboardCatalogSession.Store(catalog);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                catalog = new DashboardCatalog(
                    Array.Empty<DashboardModule>(),
                    Array.Empty<DashboardInstalledPackage>(),
                    new[]
                    {
                        new DashboardDiagnostic(
                            DashboardDiagnosticSeverity.Error,
                            "catalog-refresh-failed",
                            exception.Message,
                            string.Empty)
                    });
            }

            ApplyCatalog(catalog);
        }

        private void ApplyCatalog(DashboardCatalog catalog)
        {
            DeactivateWorkspacePanel();
            _catalog = catalog ?? DashboardCatalog.Empty;
            _installedPackageViews = _catalog.InstalledPackages
                .Where(ShouldShowInstalledPackage)
                .Select(CreateInstalledPackageView)
                .ToArray();
            _catalogLoading = false;
            RebuildWorkspacePanelOrder();

            _runtimeDiagnostics.Clear();
            _failedWorkspacePanels.Clear();
            _workspaceRegistry = DashboardWorkspaceRegistry.Build(_catalog);
            _actionRegistry = DashboardActionRegistry.Build(_catalog);
            foreach (DashboardDiagnostic diagnostic in _workspaceRegistry.Diagnostics)
                _runtimeDiagnostics["workspace/" + diagnostic.ModuleId + "/" + diagnostic.EntryId + "/" + diagnostic.Code] = diagnostic;
            foreach (DashboardDiagnostic diagnostic in _actionRegistry.Diagnostics)
                _runtimeDiagnostics["action/" + diagnostic.ModuleId + "/" + diagnostic.EntryId + "/" + diagnostic.Code] = diagnostic;
            if (_catalog.Diagnostics.Count > 0)
            {
                Debug.LogWarning(
                    "[ZeroEngine Dashboard] Catalog refreshed with " +
                    _catalog.Diagnostics.Count +
                    " diagnostic(s). Open the Diagnostics page for details.");
            }

            if (!string.IsNullOrEmpty(_selectedPanelFullId) &&
                !_catalog.VisibleWorkspaceModules.SelectMany(module => module.Panels)
                    .Any(panel => string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal)))
            {
                _selectedPanelFullId = string.Empty;
                DeactivateWorkspacePanel();
                SaveViewState();
            }
            if (_page == 2)
                RestoreHelpSelectionFromSelectedPanel();
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (_catalogRefreshQueued && _hasDrawnShell)
            {
                RefreshCatalogNow();
                return;
            }
            if (_page != 0 || _activePanel == null || _activePanelContext == null)
                return;
            float interval = _activePanel.RefreshInterval;
            if (interval <= 0f || EditorApplication.timeSinceStartup < _nextPanelTick)
                return;

            try
            {
                _activePanel.Tick(_activePanelContext, EditorApplication.timeSinceStartup);
                _nextPanelTick = EditorApplication.timeSinceStartup + interval;
                Repaint();
            }
            catch (Exception exception)
            {
                RecordWorkspaceFailure("workspace-panel-tick-failed", exception);
            }
        }

        private void OnGUI()
        {
            EditorUiStyles.EnsureCurrent();
            DrawHeader();
            DrawNavigation();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(DashboardPageHorizontalInset);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                    DrawActivePage();
                GUILayout.Space(DashboardPageHorizontalInset);
            }

            if (!_hasDrawnShell)
            {
                _hasDrawnShell = true;
                if (_deferRestoredPanelActivation)
                    Repaint();
            }
        }

        private void DrawActivePage()
        {
            if (_catalogLoading && _catalog.Modules.Count == 0 && _catalog.InstalledPackages.Count == 0)
            {
                EditorGUILayout.HelpBox(DashboardText.LoadingCatalog, MessageType.Info);
            }
            else
            {
                switch (_page)
                {
                    case 0:
                        DrawHome();
                        break;
                    case 1:
                        DeactivateWorkspacePanel();
                        DrawSystem();
                        break;
                    default:
                        DeactivateWorkspacePanel();
                        DrawHelp();
                        break;
                }
            }
        }

        private void DrawHeader()
        {
            bool compact = EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact;
            EditorUiGUILayout.CompactHeader(
                DashboardText.WindowTitle,
                compact ? string.Empty : DashboardText.HeaderSubtitle,
                drawTrailing: () =>
                {
                    int diagnosticCount = _catalog.Diagnostics.Count + _runtimeDiagnostics.Count;
                    DrawInlineMetric(DashboardText.IssueCount(diagnosticCount), diagnosticCount == 0 ? SuccessColor : WarningColor);
                    if (GUILayout.Button(
                            EditorGUIUtility.IconContent("Refresh", DashboardText.RefreshTooltip),
                            GUILayout.Width(30f),
                            GUILayout.Height(24f)))
                    {
                        InvalidateAndQueueCatalogRefresh();
                    }
                });
        }

        private void DrawNavigation()
        {
            EditorGUILayout.Space(4f);
            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                   GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                   EditorStyles.textField;
            if (UsesStackedTopNavigation(position.width))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(DashboardPageHorizontalInset);
                    DrawPageNavigation(GUILayout.ExpandWidth(true), GUILayout.Height(24f));
                    GUILayout.Space(DashboardPageHorizontalInset);
                }
                EditorGUILayout.Space(2f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(DashboardPageHorizontalInset);
                    DrawSearchControls(searchStyle);
                    GUILayout.Space(DashboardPageHorizontalInset);
                }
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(DashboardPageHorizontalInset);
                    DrawPageNavigation(GUILayout.Width(224f), GUILayout.Height(24f));
                    DrawSearchControls(searchStyle);
                    GUILayout.Space(DashboardPageHorizontalInset);
                }
            }
            EditorGUILayout.Space(6f);
        }

        private void DrawPageNavigation(params GUILayoutOption[] options)
        {
            int nextPage = GUILayout.Toolbar(_page, PageNames, options);
            if (nextPage == _page)
                return;

            _page = nextPage;
            _showContext = false;
            if (_page == 2)
                RestoreHelpSelectionFromSelectedPanel();
            SaveViewState();
        }

        private void DrawSearchControls(GUIStyle searchStyle)
        {
            DrawSearchField(searchStyle);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_search)))
            {
                if (GUILayout.Button(
                        new GUIContent(DashboardText.Clear, DashboardText.ClearTooltip),
                        GUILayout.Width(48f),
                        GUILayout.Height(22f)))
                    _search = string.Empty;
            }
        }

        private void DrawSearchField(GUIStyle searchStyle)
        {
            GUI.SetNextControlName("ZGS.Workbench.Search");
            Rect rect = GUILayoutUtility.GetRect(
                140f,
                22f,
                searchStyle,
                GUILayout.MinWidth(80f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(22f));
            _search = GUI.TextField(rect, _search ?? string.Empty, searchStyle);
            GUIContent overlay = string.IsNullOrEmpty(_search)
                ? new GUIContent(DashboardText.SearchPlaceholder, DashboardText.SearchTooltip)
                : new GUIContent(string.Empty, DashboardText.SearchTooltip);
            GUI.Label(rect, overlay, EditorStyles.centeredGreyMiniLabel);
            if (_focusSearch)
            {
                EditorGUI.FocusTextInControl("ZGS.Workbench.Search");
                _focusSearch = false;
            }
        }

        private void DrawEmbeddedToolLibrary()
        {
            IReadOnlyList<DashboardModule> modules = _catalog.VisibleModules;
            if (modules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    DashboardText.NoDeclaredTools,
                    MessageType.Info);
                return;
            }

            DrawToolFilters(modules);

            EnsureSelectedCategory(modules);
            DrawCompactCategorySelector(modules);
            DrawToolContent(modules);
        }

        private void DrawToolFilters(IReadOnlyList<DashboardModule> modules)
        {
            var scopeIds = new List<string> { string.Empty, "universal" };
            var scopeLabels = new List<GUIContent>
            {
                new GUIContent(DashboardText.All, DashboardText.ScopeTooltip),
                new GUIContent(DashboardText.Universal, DashboardText.ToolLibraryTooltip)
            };
            foreach (IGrouping<string, DashboardModule> group in modules
                         .Where(module => module.Scope == DashboardModuleScope.Project)
                         .GroupBy(module => module.ProjectId, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                DashboardModule module = group.First();
                scopeIds.Add(module.ProjectId);
                scopeLabels.Add(new GUIContent(
                    module.ProjectDisplayName,
                    DashboardText.ProjectScopeTooltip(module.ProjectDisplayName)));
            }

            int scopeIndex = scopeIds.FindIndex(id => string.Equals(id, _selectedScopeId, StringComparison.Ordinal));
            if (scopeIndex < 0)
                scopeIndex = 0;
            bool compact = EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent(DashboardText.Scope, DashboardText.ScopeTooltip), GUILayout.Width(32f));
                int selected = EditorGUILayout.Popup(scopeIndex, scopeLabels.ToArray(), GUILayout.Width(120f));
                _selectedScopeId = scopeIds[Mathf.Clamp(selected, 0, scopeIds.Count - 1)];
                GUILayout.Space(EditorUiTokens.SpaceSm);
                _showAdvanced = GUILayout.Toggle(
                    _showAdvanced,
                    new GUIContent(DashboardText.AdvancedTools, DashboardText.AdvancedToolsTooltip),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(80f));
                _showMaintenance = GUILayout.Toggle(
                    _showMaintenance,
                    new GUIContent(DashboardText.MaintenanceTools, DashboardText.MaintenanceToolsTooltip),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
                if (!compact)
                    DrawSafetyAndAvailabilityFilters();
            }
            if (compact)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                    DrawSafetyAndAvailabilityFilters();
            }
            if (_showMaintenance)
                EditorGUILayout.HelpBox(DashboardText.MaintenanceWarning, MessageType.Warning);
        }

        private void DrawSafetyAndAvailabilityFilters()
        {
            string[] safetyIds = { string.Empty, "navigation", "read-only", "project-write", "destructive" };
            GUIContent[] safetyLabels =
            {
                new GUIContent(DashboardText.All, DashboardText.SafetyFilterTooltip),
                new GUIContent(DashboardText.Navigation, DashboardText.NavigationTooltip),
                new GUIContent(DashboardText.ReadOnly, DashboardText.ReadOnlyTooltip),
                new GUIContent(DashboardText.ProjectWrite, DashboardText.ProjectWriteTooltip),
                new GUIContent(DashboardText.Destructive, DashboardText.DestructiveTooltip)
            };
            int safetyIndex = Math.Max(0, Array.IndexOf(safetyIds, _selectedSafetyId));
            GUILayout.Label(new GUIContent(DashboardText.SafetyFilter, DashboardText.SafetyFilterTooltip), GUILayout.Width(32f));
            safetyIndex = EditorGUILayout.Popup(safetyIndex, safetyLabels, GUILayout.Width(100f));
            _selectedSafetyId = safetyIds[Mathf.Clamp(safetyIndex, 0, safetyIds.Length - 1)];

            string[] availabilityIds = { string.Empty, "available", "unavailable" };
            GUIContent[] availabilityLabels =
            {
                new GUIContent(DashboardText.All, DashboardText.AvailabilityFilterTooltip),
                new GUIContent(DashboardText.Available, DashboardText.AvailabilityFilterTooltip),
                new GUIContent(DashboardText.Unavailable, DashboardText.AvailabilityFilterTooltip)
            };
            int availabilityIndex = Math.Max(0, Array.IndexOf(availabilityIds, _selectedAvailabilityId));
            GUILayout.Label(new GUIContent(DashboardText.AvailabilityFilter, DashboardText.AvailabilityFilterTooltip), GUILayout.Width(32f));
            availabilityIndex = EditorGUILayout.Popup(availabilityIndex, availabilityLabels, GUILayout.Width(88f));
            _selectedAvailabilityId = availabilityIds[Mathf.Clamp(availabilityIndex, 0, availabilityIds.Length - 1)];
        }

        private void DrawCategoryList(IReadOnlyList<DashboardModule> modules)
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.Width(EditorUiTokens.DashboardSidebarWidth)))
            {
                GUILayout.Label(DashboardText.TaskCategory, EditorStyles.boldLabel);
                _moduleScroll = EditorGUILayout.BeginScrollView(_moduleScroll);
                for (int index = 0; index < ToolCategoryIds.Length; index++)
                {
                    string categoryId = ToolCategoryIds[index];
                    GUIContent category = ToolCategoryNames[index];
                    int count = CountVisibleSurfaces(modules, categoryId);
                    if (count == 0)
                        continue;
                    if (DrawSelectionButton(
                            category.text,
                            category.tooltip,
                            count,
                            string.Equals(_selectedCategoryId, categoryId, StringComparison.Ordinal)))
                    {
                        _selectedCategoryId = categoryId;
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCompactCategorySelector(IReadOnlyList<DashboardModule> modules)
        {
            var ids = new List<string>();
            var labels = new List<GUIContent>();
            for (int index = 0; index < ToolCategoryIds.Length; index++)
            {
                int count = CountVisibleSurfaces(modules, ToolCategoryIds[index]);
                if (count == 0)
                    continue;
                GUIContent category = ToolCategoryNames[index];
                ids.Add(ToolCategoryIds[index]);
                labels.Add(new GUIContent(
                    category.text + "（" + count + "）",
                    category.tooltip));
            }

            if (ids.Count == 0)
                return;
            int currentIndex = Math.Max(0, ids.FindIndex(id => string.Equals(id, _selectedCategoryId, StringComparison.Ordinal)));
            int selected = EditorGUILayout.Popup(
                new GUIContent(DashboardText.TaskCategory, DashboardText.TaskCategoryTooltip),
                currentIndex,
                labels.ToArray());
            _selectedCategoryId = ids[Mathf.Clamp(selected, 0, ids.Count - 1)];
            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
        }

        private static bool DrawSelectionButton(string label, string tooltip, int toolCount, bool selected)
        {
            return EditorUiGUILayout.SelectionButton(
                new GUIContent(label + "  ·  " + toolCount, tooltip),
                selected,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(30f));
        }

        private void DrawToolContent(IReadOnlyList<DashboardModule> modules)
        {
            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
            SurfaceView[] surfaces = BuildSurfaceViews(modules, _selectedCategoryId, primaryOnly: false).ToArray();
            for (int index = 0; index < surfaces.Length; index++)
            {
                DrawSurfaceRow(surfaces[index]);
                if (index < surfaces.Length - 1)
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
            }

            if (surfaces.Length == 0)
                EditorGUILayout.HelpBox(DashboardText.NoSearchResults, MessageType.Info);
            DrawReferenceSearchResults(modules);
            DrawHiddenSearchSummary(modules);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHiddenSearchSummary(IEnumerable<DashboardModule> modules)
        {
            if (string.IsNullOrEmpty(_search))
                return;

            var hidden = new List<DashboardEntry>();
            bool needsCategoryOrScope = false;
            bool needsAdvanced = false;
            bool needsMaintenance = false;
            bool needsSafetyOrAvailability = false;
            foreach (DashboardModule module in modules)
            {
                foreach (DashboardEntry entry in module.VisibleActions.Where(EntryMatchesSearch))
                {
                    bool scopeVisible = ModuleMatchesScope(module);
                    bool categoryVisible = string.Equals(EffectiveCategory(entry), _selectedCategoryId, StringComparison.Ordinal);
                    bool visibilityVisible = EntryMatchesVisibility(entry);
                    bool safetyVisible = EntryMatchesSafetyFilter(entry);
                    bool availabilityVisible = EntryMatchesAvailabilityFilter(entry);
                    if (scopeVisible && categoryVisible && visibilityVisible && safetyVisible && availabilityVisible)
                        continue;
                    hidden.Add(entry);
                    needsCategoryOrScope |= !scopeVisible || !categoryVisible;
                    needsAdvanced |= entry.Visibility == DashboardEntryVisibility.Advanced && !_showAdvanced;
                    needsMaintenance |= entry.Visibility == DashboardEntryVisibility.Maintenance && !_showMaintenance;
                    needsSafetyOrAvailability |= !safetyVisible || !availabilityVisible;
                }
            }

            int count = hidden.Select(entry => entry.FullId).Distinct(StringComparer.Ordinal).Count();
            if (count == 0)
                return;
            var actions = new List<string>();
            if (needsCategoryOrScope) actions.Add(DashboardText.SwitchCategoryOrScope);
            if (needsAdvanced) actions.Add(DashboardText.EnableAdvanced);
            if (needsMaintenance) actions.Add(DashboardText.EnableMaintenance);
            if (needsSafetyOrAvailability) actions.Add(DashboardText.ChangeSafetyOrState);
            EditorGUILayout.HelpBox(
                DashboardText.HiddenMatches(count, string.Join("、", actions)),
                MessageType.Info);
        }

        private int CountVisibleSurfaces(IEnumerable<DashboardModule> modules, string categoryId)
        {
            return modules.Where(ModuleMatchesScope)
                .SelectMany(module => module.VisibleSurfaces)
                .Count(surface => surface.Entries.Any(entry =>
                    string.Equals(EffectiveCategory(entry), categoryId, StringComparison.Ordinal) &&
                    EntryMatchesVisibility(entry) && EntryMatchesSafetyFilter(entry) &&
                    EntryMatchesAvailabilityFilter(entry)));
        }

        private void EnsureSelectedCategory(IEnumerable<DashboardModule> modules)
        {
            if (CountVisibleSurfaces(modules, _selectedCategoryId) > 0)
                return;
            _selectedCategoryId = ToolCategoryIds.FirstOrDefault(id => CountVisibleSurfaces(modules, id) > 0) ??
                                  ToolCategoryIds[0];
        }

        private bool ModuleMatchesScope(DashboardModule module)
        {
            if (string.IsNullOrEmpty(_selectedScopeId))
                return true;
            if (string.Equals(_selectedScopeId, "universal", StringComparison.Ordinal))
                return module.Scope == DashboardModuleScope.Universal;
            return module.Scope == DashboardModuleScope.Project &&
                   string.Equals(module.ProjectId, _selectedScopeId, StringComparison.Ordinal);
        }

        private bool EntryMatchesVisibility(DashboardEntry entry)
        {
            return entry.Visibility == DashboardEntryVisibility.Primary ||
                   (entry.Visibility == DashboardEntryVisibility.Advanced && _showAdvanced) ||
                   (entry.Visibility == DashboardEntryVisibility.Maintenance && _showMaintenance);
        }

        private bool EntryMatchesSafetyFilter(DashboardEntry entry)
        {
            return string.IsNullOrEmpty(_selectedSafetyId) ||
                   string.Equals(_selectedSafetyId, SafetyId(entry.Safety), StringComparison.Ordinal);
        }

        private bool EntryMatchesAvailabilityFilter(DashboardEntry entry)
        {
            if (string.IsNullOrEmpty(_selectedAvailabilityId))
                return true;
            bool available = IsAvailable(entry);
            return string.Equals(_selectedAvailabilityId, available ? "available" : "unavailable", StringComparison.Ordinal);
        }

        private static string EffectiveCategory(DashboardEntry entry)
        {
            if (!entry.IsLegacy)
                return entry.Category;
            switch (entry.Category)
            {
                case "setup":
                case "documentation":
                    return "system-setup";
                default:
                    return entry.Category;
            }
        }

        private IEnumerable<SurfaceView> BuildSurfaceViews(
            IEnumerable<DashboardModule> modules,
            string categoryId,
            bool primaryOnly)
        {
            foreach (DashboardModule module in modules.Where(ModuleMatchesScope))
            {
                foreach (DashboardSurface surface in module.VisibleSurfaces)
                {
                    DashboardEntry[] entries = surface.Entries
                        .Where(entry => entry.ContentType == DashboardEntryContentType.Action)
                        .Where(entry => !IsWorkspaceNavigationDuplicate(entry))
                        .Where(entry => primaryOnly
                            ? entry.Visibility == DashboardEntryVisibility.Primary
                            : string.Equals(EffectiveCategory(entry), categoryId, StringComparison.Ordinal) &&
                              EntryMatchesVisibility(entry) && EntryMatchesSafetyFilter(entry) &&
                              EntryMatchesAvailabilityFilter(entry))
                        .ToArray();
                    if (entries.Length == 0)
                        continue;
                    if (!string.IsNullOrEmpty(_search) &&
                        !SurfaceMatchesSearch(surface) && !ModuleTextMatchesSearch(module) &&
                        !entries.Any(EntryMatchesSearch))
                    {
                        continue;
                    }

                    DashboardEntry defaultEntry = entries.Contains(surface.DefaultEntry)
                        ? surface.DefaultEntry
                        : entries[0];
                    yield return new SurfaceView(module, surface, entries, defaultEntry);
                }
            }
        }

        private void DrawSurfaceRow(SurfaceView view)
        {
            ActionUiState defaultState = EvaluateAction(view.DefaultEntry);
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        if (GUILayout.Button(
                                new GUIContent(view.Surface.DisplayName, DashboardText.ContextTooltip),
                                EditorStyles.boldLabel))
                        {
                            OpenHelp(view.Module, view.Surface, null);
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorUiGUILayout.Chip(new GUIContent(view.Module.DisplayName, view.Module.Description));
                            string owner = SurfaceContextLabel(view.Surface);
                            if (!string.IsNullOrEmpty(owner))
                                EditorUiGUILayout.Chip(owner);
                            if (view.Entries.Any(entry => entry.IsLegacy))
                                EditorUiGUILayout.Chip(new GUIContent(DashboardText.LegacyEntry, DashboardText.LegacyEntryTooltip));
                            if (view.DefaultEntry.Safety != DashboardEntrySafety.Navigation)
                                EditorUiGUILayout.Chip(new GUIContent(
                                    SafetyLabel(view.DefaultEntry.Safety),
                                    SafetyTooltip(view.DefaultEntry.Safety)));
                        }
                    }

                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(view.Entries.Count > 1 ? 190f : 112f)))
                    {
                        DrawPrimaryAction(view.DefaultEntry, defaultState);
                        if (view.Entries.Count > 1)
                            DrawMoreActions(view);
                    }
                }

                if (!defaultState.Enabled && !string.IsNullOrWhiteSpace(defaultState.DisabledReason))
                    GUILayout.Label(defaultState.DisabledReason, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawPrimaryAction(DashboardEntry entry, ActionUiState state)
        {
            string label = state.IsChecked ? "✓ " + ActionLabel(entry) : ActionLabel(entry);
            string tooltip = string.IsNullOrWhiteSpace(state.DisabledReason)
                ? entry.Description
                : entry.Description + "\n" + state.DisabledReason;
            using (new EditorGUI.DisabledScope(!state.Enabled))
            {
                if (EditorUiGUILayout.PrimaryButton(
                        new GUIContent(label, tooltip),
                        GUILayout.ExpandWidth(true),
                        GUILayout.Height(EditorUiTokens.PrimaryButtonHeight)))
                {
                    ExecuteEntry(entry);
                }
            }
        }

        private void DrawMoreActions(SurfaceView view)
        {
            if (!GUILayout.Button(
                    new GUIContent(DashboardText.More, DashboardText.MoreTooltip),
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(EditorUiTokens.PrimaryButtonHeight)))
            {
                return;
            }

            var menu = new GenericMenu();
            foreach (DashboardEntry entry in view.Entries.Where(entry => entry != view.DefaultEntry))
            {
                DashboardEntry captured = entry;
                ActionUiState state = EvaluateAction(captured);
                string label = ActionLabel(captured) + " · " + SafetyLabel(captured.Safety);
                GUIContent content = new GUIContent(label);
                if (state.Enabled)
                    menu.AddItem(content, state.IsChecked, () => ExecuteEntry(captured));
                else
                    menu.AddDisabledItem(content, state.IsChecked);
            }
            menu.ShowAsContext();
        }

        private ActionUiState EvaluateAction(DashboardEntry entry)
        {
            bool enabled = IsAvailable(entry);
            bool isChecked = false;
            string disabledReason = enabled
                ? string.Empty
                : entry.Availability == DashboardEntryAvailability.EditMode
                    ? DashboardText.EditModeOnly(entry.DisplayName)
                    : DashboardText.PlayModeOnly(entry.DisplayName);

            DashboardDiagnostic existing = FindActionDiagnostic(entry);
            if (existing != null)
                return new ActionUiState(false, false, existing.Message);

            if (entry.ExecutionKind == DashboardEntryExecutionKind.Provider && _actionRegistry != null)
            {
                if (_actionRegistry.TryGetState(entry, out EditorToolActionState state, out DashboardDiagnostic diagnostic))
                {
                    enabled &= state.Enabled;
                    isChecked = state.IsChecked;
                    if (!state.Enabled)
                        disabledReason = string.IsNullOrWhiteSpace(state.DisabledReason)
                            ? DashboardText.Unavailable
                            : state.DisabledReason;
                }
                else
                {
                    RecordActionDiagnostic(entry, diagnostic);
                    enabled = false;
                    disabledReason = diagnostic?.Message ?? DashboardText.Unavailable;
                }
            }
            return new ActionUiState(enabled, isChecked, disabledReason);
        }

        private void DrawReferenceSearchResults(IEnumerable<DashboardModule> modules)
        {
            if (string.IsNullOrEmpty(_search))
                return;
            var matches = modules.Where(ModuleMatchesScope)
                .SelectMany(module => module.VisibleReferences
                    .Where(EntryMatchesSearch)
                    .Select(entry => new ReferenceView(module, entry)))
                .ToArray();
            if (matches.Length == 0)
                return;

            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            GUILayout.Label(new GUIContent(DashboardText.ReferenceResults, DashboardText.ReferenceResultsTooltip), EditorUiStyles.SectionTitle);
            foreach (ReferenceView match in matches)
            {
                using (new EditorGUILayout.HorizontalScope(EditorUiStyles.Card))
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(match.Entry.DisplayName, EditorStyles.boldLabel);
                        GUILayout.Label(match.Entry.Description, EditorStyles.wordWrappedMiniLabel);
                        GUILayout.Label(match.Module.DisplayName, EditorStyles.miniLabel);
                    }
                    GUILayout.FlexibleSpace();
                    DrawReferenceAction(match.Entry);
                }
            }
        }

        private void DrawReferenceAction(DashboardEntry entry)
        {
            ActionUiState state = EvaluateAction(entry);
            string tooltip = string.IsNullOrWhiteSpace(state.DisabledReason)
                ? DashboardText.OpenReferenceTooltip
                : DashboardText.OpenReferenceTooltip + "\n" + state.DisabledReason;
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(120f)))
            {
                using (new EditorGUI.DisabledScope(!state.Enabled))
                {
                    if (GUILayout.Button(
                            new GUIContent(DashboardText.OpenReference, tooltip),
                            GUILayout.ExpandWidth(true),
                            GUILayout.Height(EditorUiTokens.PrimaryButtonHeight)))
                    {
                        ExecuteEntry(entry);
                    }
                }
                if (!state.Enabled && !string.IsNullOrWhiteSpace(state.DisabledReason))
                    GUILayout.Label(state.DisabledReason, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void ExecuteEntry(DashboardEntry entry)
        {
            DashboardExecutionResult result = entry.ExecutionKind == DashboardEntryExecutionKind.Provider
                ? DashboardEntryExecutor.Execute(entry, _actionRegistry, this)
                : DashboardEntryExecutor.Execute(entry);
            if (result.Status != DashboardExecutionStatus.MenuMissing &&
                result.Status != DashboardExecutionStatus.Failed)
            {
                return;
            }

            RecordActionDiagnostic(
                entry,
                result.Diagnostic ?? new DashboardDiagnostic(
                    DashboardDiagnosticSeverity.Error,
                    result.Status == DashboardExecutionStatus.MenuMissing ? "menu-execution-failed" : "action-execution-failed",
                    result.Message,
                    entry.SourcePath,
                    entry.ModuleId,
                    entry.Id,
                    entry.MenuPath));
            Repaint();
        }

        private static string ActionLabel(DashboardEntry entry)
        {
            return string.IsNullOrEmpty(entry.SurfaceActionLabel)
                ? entry.Kind == DashboardEntryKind.Window ? DashboardText.Open : DashboardText.Run
                : entry.SurfaceActionLabel;
        }

        private static string EntryTechnicalRoute(DashboardEntry entry)
        {
            return entry.IsLegacy
                ? entry.FullId + "  ·  " + entry.MenuPath
                : entry.FullId + "  ·  " + entry.ProviderId + "/" + entry.ActionId;
        }

        private DashboardDiagnostic FindActionDiagnostic(DashboardEntry entry)
        {
            return _runtimeDiagnostics.Values.FirstOrDefault(item =>
                item.Severity == DashboardDiagnosticSeverity.Error &&
                string.Equals(item.ModuleId, entry.ModuleId, StringComparison.Ordinal) &&
                string.Equals(item.EntryId, entry.Id, StringComparison.Ordinal));
        }

        private void RecordActionDiagnostic(DashboardEntry entry, DashboardDiagnostic diagnostic)
        {
            if (diagnostic == null)
                return;
            _runtimeDiagnostics["action/" + entry.FullId + "/" + diagnostic.Code] = diagnostic;
        }

        private void OpenLocalDocumentation(DashboardModule module, string documentationPath, string keySuffix)
        {
            if (File.Exists(documentationPath) || Directory.Exists(documentationPath))
            {
                EditorUtility.RevealInFinder(documentationPath);
                return;
            }

            string key = module.ModuleId + "/documentation/" + (keySuffix ?? string.Empty);
            _runtimeDiagnostics[key] = new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Warning,
                "documentation-missing",
                DashboardText.DocumentationMissing(documentationPath),
                module.Source.SourcePath,
                module.ModuleId);
        }

        private void DrawHome()
        {
            EnsureWorkspaceViewCache();
            DashboardModule[] modules = _visibleWorkspaceModules;
            SurfaceView[] primarySurfaces = BuildSurfaceViews(
                    _catalog.VisibleModules,
                    string.Empty,
                    primaryOnly: true)
                .ToArray();

            if (modules.Length == 0 && primarySurfaces.Length == 0)
            {
                DeactivateWorkspacePanel();
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_search)
                        ? DashboardText.NoDeclaredTools
                        : DashboardText.NoWorkspaceSearchResults,
                    MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(_selectedPanelFullId) &&
                !modules.SelectMany(module => module.Panels)
                    .Any(panel => string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal)))
            {
                SelectWorkspacePanel(string.Empty);
            }
            else if (!string.IsNullOrEmpty(_selectedPanelFullId) &&
                     (_helpPanel == null || !string.Equals(_helpPanel.FullId, _selectedPanelFullId, StringComparison.Ordinal)))
            {
                DashboardPanel panel = modules.SelectMany(module => module.Panels)
                    .First(item => string.Equals(item.FullId, _selectedPanelFullId, StringComparison.Ordinal));
                DashboardModule module = modules.First(item => string.Equals(item.ModuleId, panel.ModuleId, StringComparison.Ordinal));
                ShowContext(module, null, panel);
            }

            DashboardWorkspaceSplitLayout splitLayout = CalculateWorkspaceSplitLayout(
                position.width,
                _workspaceSidebarWidth);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (modules.Length > 0)
                {
                    DrawWorkspaceNavigation(_visibleWorkspaceModuleViews, splitLayout.SidebarWidth);
                    DrawWorkspaceSidebarSplitter(splitLayout.SidebarWidth);
                    EditorGUILayout.Space(EditorUiTokens.SpaceSm);
                }
                using (new EditorGUILayout.VerticalScope(
                           GUILayout.Width(splitLayout.ContentWidth),
                           GUILayout.ExpandHeight(true)))
                    DrawHomeContent(modules, primarySurfaces);
            }
        }

        private void DrawHomeContent(IReadOnlyList<DashboardModule> modules, IReadOnlyList<SurfaceView> primarySurfaces)
        {
            if (!string.IsNullOrEmpty(_selectedPanelFullId))
            {
                DrawWorkspaceContent(modules);
                return;
            }

            if (_homeView == HomeViewAllTools)
            {
                DrawEmbeddedToolLibrary();
                return;
            }

            _workspaceContentScroll = EditorGUILayout.BeginScrollView(_workspaceContentScroll);
            DrawPageTitle(DashboardText.CommonWorkflows, DashboardText.CommonWorkflowsSubtitle);
            for (int index = 0; index < primarySurfaces.Count; index++)
            {
                DrawSurfaceRow(primarySurfaces[index]);
                if (index < primarySurfaces.Count - 1)
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
            }
            if (primarySurfaces.Count == 0)
                EditorGUILayout.HelpBox(DashboardText.NoSearchResults, MessageType.Info);
            DrawReferenceSearchResults(_catalog.VisibleModules);
            EditorGUILayout.EndScrollView();
        }

        private void DrawWorkspaceNavigation(
            IReadOnlyList<WorkspaceModuleView> moduleViews,
            float sidebarWidth)
        {
            if (Event.current.type == EventType.DragExited)
            {
                _workspaceModuleDropTargetId = string.Empty;
                _workspaceDropTargetId = string.Empty;
                Repaint();
            }
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.Width(sidebarWidth)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool compactHeader = sidebarWidth < 132f;
                    if (!compactHeader)
                        GUILayout.Label(DashboardText.WorkspaceNavigation, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    bool searchActive = !string.IsNullOrEmpty(_search);
                    using (new EditorGUI.DisabledScope(searchActive || _orderedWorkspaceModules.Length == 0))
                    {
                        if (GUILayout.Button(
                                new GUIContent(
                                    DashboardText.ExpandAllGroups,
                                    DashboardText.ExpandAllGroupsTooltip),
                                EditorStyles.miniButtonLeft,
                                GUILayout.Width(26f)))
                        {
                            DashboardWorkspaceFoldout.SetAll(
                                _collapsedWorkspaceModules,
                                _orderedWorkspaceModules.Select(module => module.ModuleId),
                                expanded: true);
                            SaveViewState();
                        }
                        if (GUILayout.Button(
                                new GUIContent(
                                    DashboardText.CollapseAllGroups,
                                    DashboardText.CollapseAllGroupsTooltip),
                                EditorStyles.miniButtonRight,
                                GUILayout.Width(26f)))
                        {
                            DashboardWorkspaceFoldout.SetAll(
                                _collapsedWorkspaceModules,
                                _orderedWorkspaceModules.Select(module => module.ModuleId),
                                expanded: false);
                            SaveViewState();
                        }
                    }
                    using (new EditorGUI.DisabledScope(
                               _workspaceModuleOrder.Count == 0 && _workspacePanelOrder.Count == 0))
                    {
                        if (GUILayout.Button(
                                compactHeader
                                    ? new GUIContent("↺", DashboardText.ResetOrderTooltip)
                                    : new GUIContent(DashboardText.ResetOrder, DashboardText.ResetOrderTooltip),
                                EditorStyles.miniButton,
                                GUILayout.Width(compactHeader ? 26f : 44f)))
                        {
                            _workspaceModuleOrder.Clear();
                            _workspacePanelOrder.Clear();
                            RebuildWorkspacePanelOrder();
                            SaveViewState();
                        }
                    }
                }
                _workspaceNavigationScroll = EditorGUILayout.BeginScrollView(_workspaceNavigationScroll);
                if (EditorUiGUILayout.SelectionButton(
                        new GUIContent(DashboardText.Overview, DashboardText.OverviewTooltip),
                        string.IsNullOrEmpty(_selectedPanelFullId) && _homeView == HomeViewOverview,
                        GUILayout.ExpandWidth(true),
                        GUILayout.Height(30f)))
                {
                    SelectHomeView(HomeViewOverview);
                }
                if (EditorUiGUILayout.SelectionButton(
                        new GUIContent(DashboardText.AllTools, DashboardText.AllToolsTooltip),
                        string.IsNullOrEmpty(_selectedPanelFullId) && _homeView == HomeViewAllTools,
                        GUILayout.ExpandWidth(true),
                        GUILayout.Height(30f)))
                {
                    SelectHomeView(HomeViewAllTools);
                }
                EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                foreach (WorkspaceModuleView moduleView in moduleViews)
                {
                    if (!DrawWorkspaceModuleHeader(moduleView.Module, moduleView.Panels.Length))
                        continue;
                    foreach (WorkspacePanelView item in moduleView.Panels)
                    {
                        if (DrawWorkspacePanelTab(item.Panel))
                        {
                            SelectWorkspacePanel(item.Panel.FullId);
                            ShowContext(moduleView.Module, null, item.Panel);
                        }
                    }
                    EditorGUILayout.Space(2f);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void RebuildWorkspacePanelOrder()
        {
            var modules = new Dictionary<string, DashboardModule>(StringComparer.Ordinal);
            var defaultModuleOrder = new List<string>();
            var items = new Dictionary<string, WorkspacePanelView>(StringComparer.Ordinal);
            var defaultPanelOrder = new List<string>();
            foreach (DashboardModule module in _catalog.VisibleWorkspaceModules)
            {
                if (!modules.ContainsKey(module.ModuleId))
                {
                    modules.Add(module.ModuleId, module);
                    defaultModuleOrder.Add(module.ModuleId);
                }
                foreach (DashboardPanel panel in module.Panels)
                {
                    if (items.ContainsKey(panel.FullId))
                        continue;
                    items[panel.FullId] = new WorkspacePanelView(module, panel);
                    defaultPanelOrder.Add(panel.FullId);
                }
            }

            _orderedWorkspaceModules = DashboardWorkspaceOrder.Visible(_workspaceModuleOrder, defaultModuleOrder)
                .Where(modules.ContainsKey)
                .Select(id => modules[id])
                .ToArray();
            string[] orderedPanelIds = DashboardWorkspaceOrder.Visible(_workspacePanelOrder, defaultPanelOrder);
            _workspacePanelsById.Clear();
            foreach (KeyValuePair<string, WorkspacePanelView> item in items)
                _workspacePanelsById.Add(item.Key, item.Value);
            _workspacePanelsByModule.Clear();
            foreach (DashboardModule module in _orderedWorkspaceModules)
            {
                _workspacePanelsByModule[module.ModuleId] = orderedPanelIds
                    .Where(items.ContainsKey)
                    .Select(id => items[id])
                    .Where(item => string.Equals(item.Module.ModuleId, module.ModuleId, StringComparison.Ordinal))
                    .ToArray();
            }
            _orderedWorkspacePanels = _orderedWorkspaceModules
                .SelectMany(module => _workspacePanelsByModule[module.ModuleId])
                .ToArray();
            _workspaceViewDirty = true;
        }

        private void EnsureWorkspaceViewCache()
        {
            string search = _search ?? string.Empty;
            if (!_workspaceViewDirty &&
                ReferenceEquals(_workspaceViewCatalog, _catalog) &&
                ReferenceEquals(_workspaceViewRegistry, _workspaceRegistry) &&
                string.Equals(_workspaceViewSearch, search, StringComparison.Ordinal))
            {
                return;
            }

            _visibleWorkspaceModules = _catalog.VisibleWorkspaceModules
                .Where(module => ModulePanelMatchesSearch(module))
                .Select(module => new DashboardModule(
                    module.ModuleId,
                    module.DisplayName,
                    module.Description,
                    module.Order,
                    module.DocumentationPath,
                    module.DocumentationUrl,
                    module.Source,
                    module.Entries,
                    panels: module.Panels.Where(panel =>
                            (_workspaceRegistry?.IsAvailable(panel) ?? false) && PanelMatchesSearch(panel))
                        .ToArray()))
                .Where(module => module.Panels.Count > 0)
                .ToArray();

            var visibleModules = _visibleWorkspaceModules.ToDictionary(
                module => module.ModuleId,
                StringComparer.Ordinal);
            var moduleViews = new List<WorkspaceModuleView>();
            var visiblePanels = new List<WorkspacePanelView>();
            foreach (DashboardModule orderedModule in _orderedWorkspaceModules)
            {
                if (!visibleModules.TryGetValue(orderedModule.ModuleId, out DashboardModule visibleModule) ||
                    !_workspacePanelsByModule.TryGetValue(orderedModule.ModuleId, out WorkspacePanelView[] orderedPanels))
                {
                    continue;
                }

                var visiblePanelIds = new HashSet<string>(
                    visibleModule.Panels.Select(panel => panel.FullId),
                    StringComparer.Ordinal);
                WorkspacePanelView[] panels = orderedPanels
                    .Where(item => visiblePanelIds.Contains(item.Panel.FullId))
                    .ToArray();
                moduleViews.Add(new WorkspaceModuleView(visibleModule, panels));
                visiblePanels.AddRange(panels);
            }

            _visibleWorkspaceModuleViews = moduleViews.ToArray();
            _visibleWorkspacePanels = visiblePanels.ToArray();
            _workspaceViewCatalog = _catalog;
            _workspaceViewRegistry = _workspaceRegistry;
            _workspaceViewSearch = search;
            _workspaceViewDirty = false;
        }

        private bool DrawWorkspaceModuleHeader(DashboardModule module, int panelCount)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, 26f, GUILayout.ExpandWidth(true));
            Rect handleRect = new Rect(rowRect.x + 2f, rowRect.y, 18f, rowRect.height);
            Rect countRect = new Rect(
                rowRect.xMax - WorkspaceNavigationRightInset - 28f,
                rowRect.y,
                28f,
                rowRect.height);
            Rect foldoutRect = new Rect(
                handleRect.xMax + 2f,
                rowRect.y,
                Mathf.Max(1f, countRect.x - handleRect.xMax - 4f),
                rowRect.height);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rowRect, EditorUiPalette.Current.RaisedSurface);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
            GUI.Label(
                handleRect,
                new GUIContent("≡", DashboardText.ReorderWorkspaceModulesTooltip),
                EditorStyles.centeredGreyMiniLabel);
            GUI.Label(
                countRect,
                new GUIContent(panelCount.ToString(), DashboardText.WorkspaceNavigationTooltip),
                EditorStyles.centeredGreyMiniLabel);

            bool searchActive = !string.IsNullOrEmpty(_search);
            bool expanded = DashboardWorkspaceFoldout.IsExpanded(
                _collapsedWorkspaceModules,
                module.ModuleId,
                searchActive);
            bool nextExpanded = EditorGUI.Foldout(
                foldoutRect,
                expanded,
                new GUIContent(
                    module.DisplayName,
                    DashboardText.WorkspaceGroupTooltip(module.Description, searchActive)),
                toggleOnLabelClick: true);
            if (!searchActive && nextExpanded != expanded)
            {
                if (nextExpanded)
                    _collapsedWorkspaceModules.Remove(module.ModuleId);
                else
                    _collapsedWorkspaceModules.Add(module.ModuleId);
                expanded = nextExpanded;
                SaveViewState();
            }

            HandleWorkspaceModuleDrag(handleRect, rowRect, module.ModuleId);
            if (Event.current.type == EventType.Repaint &&
                string.Equals(_workspaceModuleDropTargetId, module.ModuleId, StringComparison.Ordinal))
            {
                float y = _workspaceModuleDropBefore ? rowRect.y : rowRect.yMax - 2f;
                EditorGUI.DrawRect(new Rect(rowRect.x, y, rowRect.width, 2f), AccentColor);
            }
            return expanded;
        }

        private void DrawWorkspaceSidebarSplitter(float sidebarWidth)
        {
            Rect splitterRect = GUILayoutUtility.GetRect(
                WorkspaceSidebarSplitterWidth,
                WorkspaceSidebarSplitterWidth,
                GUILayout.Width(WorkspaceSidebarSplitterWidth),
                GUILayout.ExpandHeight(true));
            GUI.Label(
                splitterRect,
                new GUIContent(string.Empty, DashboardText.ResizeWorkspaceNavigationTooltip));
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            int controlId = GUIUtility.GetControlID(FocusType.Passive, splitterRect);
            Event current = Event.current;
            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when current.button == 0 && splitterRect.Contains(current.mousePosition):
                    GUIUtility.hotControl = controlId;
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    _workspaceSidebarWidth = Mathf.Clamp(
                        sidebarWidth + current.delta.x,
                        WorkspaceSidebarMinWidth,
                        WorkspaceSidebarMaxWidth);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    SaveViewState();
                    current.Use();
                    break;
                case EventType.Repaint:
                    EditorGUI.DrawRect(
                        new Rect(splitterRect.center.x, splitterRect.y, 1f, splitterRect.height),
                        EditorUiPalette.Current.Border);
                    break;
            }
        }

        private bool IsWorkspaceNavigationDuplicate(DashboardEntry entry)
        {
            if (entry == null || entry.Safety != DashboardEntrySafety.Navigation || _workspaceRegistry == null)
                return false;
            return _workspacePanelsById.TryGetValue(entry.FullId, out WorkspacePanelView view) &&
                   _workspaceRegistry.IsAvailable(view.Panel);
        }

        internal static DashboardWorkspaceSplitLayout CalculateWorkspaceSplitLayout(
            float windowWidth,
            float preferredSidebarWidth)
        {
            return DashboardWorkspaceLayout.CalculateSplitLayout(
                windowWidth,
                preferredSidebarWidth,
                DashboardPageHorizontalInset * 2f + WorkspaceSidebarSplitterWidth +
                EditorUiTokens.SpaceSm + 16f,
                WorkspaceSidebarMinWidth,
                WorkspaceSidebarMaxWidth,
                WorkspaceSidebarMaximumFraction);
        }

        internal static bool UsesStackedTopNavigation(float width)
        {
            return width < StackedNavigationMaxWidth;
        }

        private void HandleWorkspaceModuleDrag(Rect handleRect, Rect rowRect, string moduleId)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && handleRect.Contains(current.mousePosition))
            {
                _pressedWorkspaceModuleId = moduleId;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag &&
                string.Equals(_pressedWorkspaceModuleId, moduleId, StringComparison.Ordinal))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(WorkspaceModuleDragDataKey, moduleId);
                DragAndDrop.StartDrag(DashboardText.ReorderWorkspaceModules);
                _pressedWorkspaceModuleId = string.Empty;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseUp)
                _pressedWorkspaceModuleId = string.Empty;

            string draggedModuleId = DragAndDrop.GetGenericData(WorkspaceModuleDragDataKey) as string;
            if (string.IsNullOrEmpty(draggedModuleId) ||
                string.Equals(draggedModuleId, moduleId, StringComparison.Ordinal) ||
                !rowRect.Contains(current.mousePosition))
            {
                return;
            }
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;

            _workspaceModuleDropTargetId = moduleId;
            _workspaceModuleDropBefore = current.mousePosition.y < rowRect.center.y;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ReorderWorkspaceModule(draggedModuleId, moduleId, _workspaceModuleDropBefore);
                _workspaceModuleDropTargetId = string.Empty;
            }
            current.Use();
            Repaint();
        }

        private void ReorderWorkspaceModule(string draggedModuleId, string targetModuleId, bool before)
        {
            string[] defaultOrder = _catalog.VisibleWorkspaceModules
                .Select(module => module.ModuleId)
                .ToArray();
            string[] next = DashboardWorkspaceOrder.Move(
                _workspaceModuleOrder,
                defaultOrder,
                draggedModuleId,
                targetModuleId,
                before);
            _workspaceModuleOrder.Clear();
            _workspaceModuleOrder.AddRange(next);
            RebuildWorkspacePanelOrder();
            SaveViewState();
        }

        private bool DrawWorkspacePanelTab(DashboardPanel panel)
        {
            Rect rowRect = GUILayoutUtility.GetRect(1f, 28f, GUILayout.ExpandWidth(true));
            DashboardWorkspacePanelLayout layout = DashboardWorkspaceLayout.CalculatePanelLayout(
                rowRect,
                WorkspacePanelHandleInset,
                WorkspacePanelHandleWidth,
                WorkspacePanelGap,
                WorkspaceNavigationRightInset,
                WorkspacePanelVerticalInset,
                WorkspaceSelectionBarWidth);
            Rect handleRect = layout.HandleRect;
            Rect buttonRect = layout.ButtonRect;
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.Pan);
            bool showHandle = handleRect.Contains(Event.current.mousePosition) ||
                              string.Equals(_pressedWorkspacePanelId, panel.FullId, StringComparison.Ordinal) ||
                              string.Equals(
                                  DragAndDrop.GetGenericData(WorkspacePanelDragDataKey) as string,
                                  panel.FullId,
                                  StringComparison.Ordinal);
            Color previousGuiColor = GUI.color;
            Color handleColor = previousGuiColor;
            handleColor.a *= showHandle ? 1f : 0.2f;
            GUI.color = handleColor;
            GUI.Label(
                handleRect,
                new GUIContent("⋮", DashboardText.ReorderWorkspacePanelsTooltip),
                EditorStyles.centeredGreyMiniLabel);
            GUI.color = previousGuiColor;

            bool selected = string.Equals(_selectedPanelFullId, panel.FullId, StringComparison.Ordinal);
            Color previous = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = EditorUiPalette.Current.Selection;
            bool clicked = GUI.Button(
                buttonRect,
                new GUIContent(panel.DisplayName, panel.Description),
                selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton);
            GUI.backgroundColor = previous;
            if (Event.current.type == EventType.Repaint && selected)
                EditorGUI.DrawRect(layout.SelectionRect, AccentColor);

            HandleWorkspacePanelDrag(handleRect, rowRect, panel.FullId);
            if (Event.current.type == EventType.Repaint &&
                string.Equals(_workspaceDropTargetId, panel.FullId, StringComparison.Ordinal))
            {
                float y = _workspaceDropBefore ? rowRect.y : rowRect.yMax - 2f;
                EditorGUI.DrawRect(new Rect(rowRect.x, y, rowRect.width, 2f), AccentColor);
            }
            return clicked;
        }

        private void HandleWorkspacePanelDrag(Rect handleRect, Rect rowRect, string panelFullId)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && handleRect.Contains(current.mousePosition))
            {
                _pressedWorkspacePanelId = panelFullId;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag &&
                string.Equals(_pressedWorkspacePanelId, panelFullId, StringComparison.Ordinal))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(WorkspacePanelDragDataKey, panelFullId);
                DragAndDrop.StartDrag(DashboardText.ReorderWorkspacePanels);
                _pressedWorkspacePanelId = string.Empty;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseUp)
                _pressedWorkspacePanelId = string.Empty;

            string draggedPanelId = DragAndDrop.GetGenericData(WorkspacePanelDragDataKey) as string;
            if (string.IsNullOrEmpty(draggedPanelId) ||
                string.Equals(draggedPanelId, panelFullId, StringComparison.Ordinal) ||
                !PanelsShareWorkspaceModule(draggedPanelId, panelFullId) ||
                !rowRect.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
                return;
            _workspaceDropTargetId = panelFullId;
            _workspaceDropBefore = current.mousePosition.y < rowRect.center.y;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ReorderWorkspacePanel(draggedPanelId, panelFullId, _workspaceDropBefore);
                _workspaceDropTargetId = string.Empty;
            }
            current.Use();
            Repaint();
        }

        private void ReorderWorkspacePanel(string draggedPanelId, string targetPanelId, bool before)
        {
            if (!PanelsShareWorkspaceModule(draggedPanelId, targetPanelId))
                return;
            string[] defaultOrder = _catalog.VisibleWorkspaceModules
                .SelectMany(module => module.Panels)
                .Select(panel => panel.FullId)
                .ToArray();
            string[] next = DashboardWorkspaceOrder.Move(
                _workspacePanelOrder,
                defaultOrder,
                draggedPanelId,
                targetPanelId,
                before);
            _workspacePanelOrder.Clear();
            _workspacePanelOrder.AddRange(next);
            RebuildWorkspacePanelOrder();
            SaveViewState();
        }

        private bool PanelsShareWorkspaceModule(string firstPanelId, string secondPanelId)
        {
            return _workspacePanelsById.TryGetValue(firstPanelId, out WorkspacePanelView first) &&
                   _workspacePanelsById.TryGetValue(secondPanelId, out WorkspacePanelView second) &&
                   string.Equals(first.Module.ModuleId, second.Module.ModuleId, StringComparison.Ordinal);
        }

        private void DrawWorkspaceContent(IReadOnlyList<DashboardModule> modules)
        {
            DashboardPanel descriptor = modules.SelectMany(module => module.Panels)
                .FirstOrDefault(panel => string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal));
            if (descriptor == null)
                return;
            DashboardModule module = modules.First(item => string.Equals(item.ModuleId, descriptor.ModuleId, StringComparison.Ordinal));

            _workspaceContentScroll = EditorGUILayout.BeginScrollView(_workspaceContentScroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    if (GUILayout.Button(
                            new GUIContent(descriptor.DisplayName, DashboardText.ContextTooltip),
                            EditorUiStyles.SectionTitle))
                    {
                        OpenHelp(module, null, descriptor);
                    }
                    EditorUiGUILayout.Chip(new GUIContent(module.DisplayName, module.Description));
                }
                GUILayout.FlexibleSpace();
                if (descriptor.Safety != DashboardEntrySafety.Navigation)
                    EditorUiGUILayout.Chip(new GUIContent(SafetyLabel(descriptor.Safety), SafetyTooltip(descriptor.Safety)));
            }
            EditorGUILayout.Space(EditorUiTokens.SpaceSm);

            if (!IsAvailable(descriptor))
            {
                DeactivateWorkspacePanel();
                EditorGUILayout.HelpBox(
                    descriptor.Availability == DashboardEntryAvailability.EditMode
                        ? DashboardText.EditModeOnly(descriptor.DisplayName)
                        : DashboardText.PlayModeOnly(descriptor.DisplayName),
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (_failedWorkspacePanels.Contains(descriptor.FullId))
            {
                EditorGUILayout.HelpBox(DashboardText.PanelLoadFailed, MessageType.Error);
                if (GUILayout.Button(new GUIContent(DashboardText.GoToDiagnostics, DashboardText.GoToDiagnosticsTooltip)))
                    _page = 2;
                if (GUILayout.Button(new GUIContent(DashboardText.Retry, DashboardText.RetryTooltip)))
                {
                    _failedWorkspacePanels.Remove(descriptor.FullId);
                    RemoveWorkspaceDiagnostics(descriptor.FullId);
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            if (_deferRestoredPanelActivation && !_hasDrawnShell)
            {
                EditorGUILayout.HelpBox(DashboardText.LoadingPanel, MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            bool needsActivation = _activePanel == null ||
                                   _activePanelDescriptor == null ||
                                   !string.Equals(
                                       _activePanelDescriptor.FullId,
                                       descriptor.FullId,
                                       StringComparison.Ordinal);
            if (needsActivation && Event.current.type != EventType.Layout)
            {
                EditorGUILayout.HelpBox(DashboardText.LoadingPanel, MessageType.Info);
                Repaint();
                EditorGUILayout.EndScrollView();
                return;
            }

            if (EnsureActiveWorkspacePanel(descriptor))
            {
                float availableWidth = CalculateWorkspaceSplitLayout(
                    position.width,
                    _workspaceSidebarWidth).ContentWidth;
                bool fullWidth = UsesFullWidthWorkspaceLayout(_activePanel);
                _activePanelContext.AvailableWidth = fullWidth
                    ? availableWidth
                    : Mathf.Min(EditorUiTokens.FormContentMaxWidth, availableWidth);
                try
                {
                    if (fullWidth)
                        _activePanel.OnGUI(_activePanelContext);
                    else
                    {
                        using (EditorUiGUILayout.ConstrainedContent())
                            _activePanel.OnGUI(_activePanelContext);
                    }
                }
                catch (Exception exception)
                {
                    RecordWorkspaceFailure("workspace-panel-draw-failed", exception);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static bool UsesFullWidthWorkspaceLayout(IEditorWorkspacePanel panel)
        {
            return panel is IEditorWorkspaceFullWidthPanel;
        }

        private bool EnsureActiveWorkspacePanel(DashboardPanel descriptor)
        {
            if (_activePanel != null && _activePanelDescriptor != null &&
                string.Equals(_activePanelDescriptor.FullId, descriptor.FullId, StringComparison.Ordinal))
            {
                return true;
            }

            DeactivateWorkspacePanel();
            IEditorWorkspacePanel panel = null;
            DashboardDiagnostic diagnostic = null;
            if (_workspaceRegistry == null ||
                !_workspaceRegistry.TryCreate(descriptor, out panel, out diagnostic))
            {
                if (diagnostic != null)
                    RecordWorkspaceDiagnostic(descriptor, diagnostic);
                _failedWorkspacePanels.Add(descriptor.FullId);
                return false;
            }

            _activePanelDescriptor = descriptor;
            _activePanel = panel;
            _activePanelContext = new EditorWorkspacePanelContext(
                this,
                descriptor.ModuleId,
                descriptor.Id,
                DrawWorkspaceAction);
            try
            {
                _activePanel.Activate(_activePanelContext);
                _nextPanelTick = EditorApplication.timeSinceStartup + Math.Max(0f, _activePanel.RefreshInterval);
                return true;
            }
            catch (Exception exception)
            {
                RecordWorkspaceFailure("workspace-panel-activate-failed", exception);
                return false;
            }
        }

        private void DeactivateWorkspacePanel()
        {
            IEditorWorkspacePanel panel = _activePanel;
            _activePanel = null;
            _activePanelContext = null;
            _activePanelDescriptor = null;
            if (panel == null)
                return;
            try
            {
                panel.Deactivate();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            try
            {
                panel.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private bool DrawWorkspaceAction(EditorWorkspaceAction action, GUILayoutOption[] options)
        {
            bool clicked;
            using (new EditorGUI.DisabledScope(!action.Enabled))
            {
                switch (action.Style)
                {
                    case EditorWorkspaceActionStyle.Primary:
                        clicked = EditorUiGUILayout.PrimaryButton(action.Content, options);
                        break;
                    case EditorWorkspaceActionStyle.Destructive:
                        clicked = EditorUiGUILayout.DestructiveButton(action.Content, options);
                        break;
                    default:
                        clicked = GUILayout.Button(action.Content, options);
                        break;
                }
            }

            if (!clicked)
                return false;
            bool needsConfirmation = action.Safety == EditorWorkspaceActionSafety.ProjectWrite ||
                                     action.Safety == EditorWorkspaceActionSafety.Destructive;
            if (needsConfirmation && string.IsNullOrWhiteSpace(action.Confirmation))
                throw new InvalidOperationException("Write-capable workspace actions require confirmation text.");
            if (!string.IsNullOrWhiteSpace(action.Confirmation) &&
                !EditorUtility.DisplayDialog(DashboardText.ConfirmAction, action.Confirmation, DashboardText.Continue, DashboardText.Cancel))
            {
                return false;
            }
            action.Execute();
            return true;
        }

        private void SelectWorkspacePanel(string fullId)
        {
            _deferRestoredPanelActivation = false;
            if (!string.IsNullOrEmpty(fullId))
                _homeView = HomeViewOverview;
            if (string.Equals(_selectedPanelFullId, fullId, StringComparison.Ordinal))
                return;
            _selectedPanelFullId = fullId ?? string.Empty;
            _workspaceContentScroll = Vector2.zero;
            DeactivateWorkspacePanel();
            DashboardPanel panel = _catalog.VisibleWorkspaceModules
                .SelectMany(module => module.Panels)
                .FirstOrDefault(item => string.Equals(item.FullId, _selectedPanelFullId, StringComparison.Ordinal));
            DashboardModule module = panel == null
                ? null
                : _catalog.Modules.FirstOrDefault(item => string.Equals(item.ModuleId, panel.ModuleId, StringComparison.Ordinal));
            if (panel == null || module == null)
            {
                if (_helpPanel != null)
                    ClearContext();
                SaveViewState();
                return;
            }
            ShowContext(module, null, panel);
            SaveViewState();
        }

        private void SelectHomeView(int view)
        {
            _homeView = Mathf.Clamp(view, HomeViewOverview, HomeViewAllTools);
            SelectWorkspacePanel(string.Empty);
            _workspaceContentScroll = Vector2.zero;
            SaveViewState();
        }

        bool IEditorWorkspaceNavigator.TryShowWorkspace(string moduleId, string panelId)
        {
            return TryShowWorkspaceInternal(moduleId, panelId);
        }

        private bool TryShowWorkspaceInternal(string moduleId, string panelId)
        {
            string fullId = (moduleId ?? string.Empty) + "/" + (panelId ?? string.Empty);
            bool exists = _catalog.VisibleWorkspaceModules
                .SelectMany(module => module.Panels)
                .Any(panel => string.Equals(panel.FullId, fullId, StringComparison.Ordinal));
            if (!exists)
                return false;

            _page = 0;
            _homeView = HomeViewOverview;
            SelectWorkspacePanel(fullId);
            SaveViewState();
            Repaint();
            return true;
        }

        private void RestoreHelpSelectionFromSelectedPanel()
        {
            if (HasContextSelection() || string.IsNullOrEmpty(_selectedPanelFullId))
                return;

            DashboardPanel panel = _catalog.VisibleWorkspaceModules
                .SelectMany(module => module.Panels)
                .FirstOrDefault(item => string.Equals(item.FullId, _selectedPanelFullId, StringComparison.Ordinal));
            DashboardModule module = panel == null
                ? null
                : _catalog.Modules.FirstOrDefault(item =>
                    string.Equals(item.ModuleId, panel.ModuleId, StringComparison.Ordinal));
            if (panel == null || module == null)
                return;

            _helpModule = module;
            _helpSurface = null;
            _helpPanel = panel;
            _showContext = true;
        }

        private void RestoreViewState()
        {
            DashboardViewState state = DashboardViewStateStore.Load();
            _page = Mathf.Clamp(state.Page, 0, PageNames.Length - 1);
            _homeView = Mathf.Clamp(state.HomeView, HomeViewOverview, HomeViewAllTools);
            _search = state.Search;
            _selectedCategoryId = state.SelectedCategoryId;
            _selectedScopeId = state.SelectedScopeId;
            _selectedSafetyId = state.SelectedSafetyId;
            _selectedAvailabilityId = state.SelectedAvailabilityId;
            _showAdvanced = state.ShowAdvanced;
            _showMaintenance = state.ShowMaintenance;
            _selectedPanelFullId = state.SelectedPanelFullId;
            _workspaceModuleOrder.Clear();
            _workspaceModuleOrder.AddRange(state.WorkspaceModuleOrder);
            _workspacePanelOrder.Clear();
            _workspacePanelOrder.AddRange(state.WorkspacePanelOrder);
            _collapsedWorkspaceModules.Clear();
            _collapsedWorkspaceModules.UnionWith(state.CollapsedWorkspaceModuleIds);
            _workspaceSidebarWidth = Mathf.Clamp(
                state.WorkspaceSidebarWidth,
                WorkspaceSidebarMinWidth,
                WorkspaceSidebarMaxWidth);
            _moduleScroll = state.ModuleScroll;
            _contentScroll = state.ContentScroll;
            _systemScroll = state.SystemScroll;
            _workspaceNavigationScroll = state.WorkspaceNavigationScroll;
            _workspaceContentScroll = state.WorkspaceContentScroll;
            _contextScroll = state.ContextScroll;
        }

        private void SaveViewState()
        {
            DashboardViewStateStore.Save(new DashboardViewState
            {
                Page = _page,
                HomeView = _homeView,
                Search = _search,
                SelectedCategoryId = _selectedCategoryId,
                SelectedScopeId = _selectedScopeId,
                SelectedSafetyId = _selectedSafetyId,
                SelectedAvailabilityId = _selectedAvailabilityId,
                ShowAdvanced = _showAdvanced,
                ShowMaintenance = _showMaintenance,
                SelectedPanelFullId = _selectedPanelFullId,
                WorkspaceModuleOrder = _workspaceModuleOrder.ToArray(),
                WorkspacePanelOrder = _workspacePanelOrder.ToArray(),
                CollapsedWorkspaceModuleIds = _collapsedWorkspaceModules
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                WorkspaceSidebarWidth = _workspaceSidebarWidth,
                ModuleScroll = _moduleScroll,
                ContentScroll = _contentScroll,
                SystemScroll = _systemScroll,
                WorkspaceNavigationScroll = _workspaceNavigationScroll,
                WorkspaceContentScroll = _workspaceContentScroll,
                ContextScroll = _contextScroll
            });
        }

        private bool ModulePanelMatchesSearch(DashboardModule module)
        {
            return string.IsNullOrEmpty(_search) ||
                   Matches(module.DisplayName) ||
                   Matches(module.Description) ||
                   Matches(module.ModuleId) ||
                   module.Panels.Any(PanelMatchesSearch);
        }

        private bool PanelMatchesSearch(DashboardPanel panel)
        {
            return Matches(panel.DisplayName) ||
                   Matches(panel.Description) ||
                   Matches(panel.Usage) ||
                   Matches(panel.Section) ||
                   Matches(panel.ProviderId) ||
                   Matches(panel.FullId);
        }

        private static bool IsAvailable(DashboardPanel panel)
        {
            return panel.Availability == DashboardEntryAvailability.Always ||
                   (panel.Availability == DashboardEntryAvailability.EditMode && !EditorApplication.isPlaying) ||
                   (panel.Availability == DashboardEntryAvailability.PlayMode && EditorApplication.isPlaying);
        }

        private void RecordWorkspaceFailure(string code, Exception exception)
        {
            DashboardPanel descriptor = _activePanelDescriptor;
            if (descriptor == null)
            {
                Debug.LogException(exception);
                return;
            }
            var diagnostic = new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                code,
                exception.GetBaseException().Message,
                descriptor.SourcePath,
                descriptor.ModuleId,
                descriptor.Id);
            _failedWorkspacePanels.Add(descriptor.FullId);
            RecordWorkspaceDiagnostic(descriptor, diagnostic);
            DeactivateWorkspacePanel();
            Repaint();
        }

        private void RecordWorkspaceDiagnostic(DashboardPanel descriptor, DashboardDiagnostic diagnostic)
        {
            _runtimeDiagnostics["workspace/" + descriptor.FullId + "/" + diagnostic.Code] = diagnostic;
        }

        private void RemoveWorkspaceDiagnostics(string fullId)
        {
            string prefix = "workspace/" + fullId + "/";
            foreach (string key in _runtimeDiagnostics.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                _runtimeDiagnostics.Remove(key);
        }

        private void ShowContext(DashboardModule module, DashboardSurface surface, DashboardPanel panel)
        {
            _helpModule = module;
            _helpSurface = surface;
            _helpPanel = panel;
            _showContext = true;
            _showDeveloperInfo = false;
            _contextScroll = Vector2.zero;
            Repaint();
        }

        private void OpenHelp(DashboardModule module, DashboardSurface surface, DashboardPanel panel)
        {
            ShowContext(module, surface, panel);
            _page = 2;
            SaveViewState();
        }

        private void DrawHelp()
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
                DrawContextContents(canClose: false);
        }

        private void ClearContext()
        {
            _helpModule = null;
            _helpSurface = null;
            _helpPanel = null;
            _showContext = false;
            _showDeveloperInfo = false;
        }

        private bool HasContextSelection()
        {
            return _helpModule != null && (_helpSurface != null || _helpPanel != null);
        }

        private void DrawContextOverlay()
        {
            EditorUiResponsiveMode mode = EditorUiGUILayout.ResponsiveMode(position.width);
            if (mode == EditorUiResponsiveMode.Wide || !_showContext || !HasContextSelection())
                return;

            float width = mode == EditorUiResponsiveMode.Compact
                ? Mathf.Max(300f, position.width - 16f)
                : 320f;
            var rect = new Rect(
                Mathf.Max(8f, position.width - width - 8f),
                92f,
                width,
                Mathf.Max(260f, position.height - 100f));
            GUILayout.BeginArea(rect, GUIContent.none, EditorUiStyles.Card);
            DrawContextContents(canClose: true);
            GUILayout.EndArea();
        }

        private void DrawContextDrawer(params GUILayoutOption[] options)
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, options))
                DrawContextContents(canClose: false);
        }

        private void DrawContextContents(bool canClose)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(DashboardText.Context, EditorUiStyles.SectionTitle);
                GUILayout.FlexibleSpace();
                if (canClose && GUILayout.Button(
                        new GUIContent(DashboardText.Close, DashboardText.CloseHelpTooltip),
                        GUILayout.Width(48f)))
                {
                    _showContext = false;
                    return;
                }
            }

            if (!HasContextSelection())
            {
                GUILayout.Label(DashboardText.ContextEmpty, EditorStyles.wordWrappedMiniLabel);
                return;
            }

            _contextScroll = EditorGUILayout.BeginScrollView(_contextScroll);
            string description = _helpPanel?.Description ?? _helpSurface?.Description ?? _helpModule.Description;
            string usage = _helpPanel?.Usage ?? _helpSurface?.Usage;
            GUILayout.Label(DashboardText.Purpose, EditorStyles.miniBoldLabel);
            GUILayout.Label(string.IsNullOrWhiteSpace(description) ? DashboardText.HeaderSubtitle : description, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            GUILayout.Label(DashboardText.CurrentState, EditorStyles.miniBoldLabel);
            DrawContextState();

            if (!string.IsNullOrWhiteSpace(usage))
            {
                EditorGUILayout.Space(EditorUiTokens.SpaceSm);
                GUILayout.Label(DashboardText.Usage, EditorStyles.miniBoldLabel);
                GUILayout.Label(usage, EditorStyles.wordWrappedLabel);
            }

            DrawRelatedResources();

            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            GUILayout.Label(DashboardText.SafetyAndImpact, EditorStyles.miniBoldLabel);
            DashboardEntrySafety safety = _helpPanel?.Safety ?? _helpSurface?.DefaultEntry.Safety ?? DashboardEntrySafety.Navigation;
            EditorUiGUILayout.Chip(new GUIContent(SafetyLabel(safety), SafetyTooltip(safety)));
            GUILayout.Label(SafetyTooltip(safety), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            _showDeveloperInfo = EditorUiGUILayout.Disclosure(
                _showDeveloperInfo,
                new GUIContent(DashboardText.DeveloperInfo, DashboardText.DetailsTooltip));
            if (_showDeveloperInfo)
            {
                DrawTechnicalValue(_helpModule.ModuleId);
                if (_helpPanel != null)
                {
                    DrawTechnicalValue(_helpPanel.FullId);
                    DrawTechnicalValue(_helpPanel.ProviderId);
                    DrawTechnicalValue(_helpPanel.SourcePath);
                }
                else if (_helpSurface != null)
                {
                    foreach (DashboardEntry entry in _helpSurface.Entries)
                        DrawTechnicalValue(EntryTechnicalRoute(entry));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawContextState()
        {
            if (_helpPanel != null)
            {
                string reason = IsAvailable(_helpPanel)
                    ? _failedWorkspacePanels.Contains(_helpPanel.FullId) ? DashboardText.PanelLoadFailed : DashboardText.Ready
                    : _helpPanel.Availability == DashboardEntryAvailability.EditMode
                        ? DashboardText.EditModeOnly(_helpPanel.DisplayName)
                        : DashboardText.PlayModeOnly(_helpPanel.DisplayName);
                GUILayout.Label(reason, EditorStyles.wordWrappedMiniLabel);
                return;
            }

            ActionUiState state = EvaluateAction(_helpSurface.DefaultEntry);
            GUILayout.Label(state.Enabled ? DashboardText.Ready : state.DisabledReason, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawRelatedResources()
        {
            DashboardEntry[] documentedEntries = _helpSurface?.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.DocumentationPath) ||
                                !string.IsNullOrEmpty(entry.DocumentationUrl))
                .ToArray() ?? Array.Empty<DashboardEntry>();
            DashboardEntry[] references = _helpModule.VisibleReferences.ToArray();
            bool hasModuleDocumentation = !string.IsNullOrEmpty(_helpModule.DocumentationPath) ||
                                          !string.IsNullOrEmpty(_helpModule.DocumentationUrl);
            if (documentedEntries.Length == 0 && references.Length == 0 && !hasModuleDocumentation)
                return;

            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            GUILayout.Label(DashboardText.RelatedResources, EditorStyles.miniBoldLabel);
            foreach (DashboardEntry entry in documentedEntries)
            {
                DrawDocumentationButtons(
                    string.IsNullOrEmpty(entry.SurfaceActionLabel) ? entry.DisplayName : entry.SurfaceActionLabel,
                    entry.DocumentationPath,
                    entry.DocumentationUrl,
                    _helpModule,
                    entry.Id);
            }
            if (hasModuleDocumentation)
            {
                DrawDocumentationButtons(
                    _helpModule.DisplayName,
                    _helpModule.DocumentationPath,
                    _helpModule.DocumentationUrl,
                    _helpModule,
                    "module");
            }
            foreach (DashboardEntry reference in references)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(reference.DisplayName, EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    DrawReferenceAction(reference);
                }
            }
        }

        private void DrawDocumentationButtons(
            string label,
            string documentationPath,
            string documentationUrl,
            DashboardModule module,
            string keySuffix)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.MinWidth(100f));
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(documentationPath) && GUILayout.Button(
                        new GUIContent(DashboardText.Documentation, DashboardText.DocumentationTooltip),
                        GUILayout.Width(64f)))
                {
                    OpenLocalDocumentation(module, documentationPath, keySuffix);
                }
                if (!string.IsNullOrEmpty(documentationUrl) && GUILayout.Button(
                        new GUIContent(DashboardText.Website, DashboardText.WebsiteTooltip),
                        GUILayout.Width(64f)))
                {
                    Application.OpenURL(documentationUrl);
                }
            }
        }

        private static void DrawTechnicalValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            float height = Mathf.Max(16f, EditorStyles.miniLabel.CalcHeight(new GUIContent(value), EditorGUIUtility.currentViewWidth - 56f));
            EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel, GUILayout.Height(height));
        }

        private void DrawSystem()
        {
            _systemScroll = EditorGUILayout.BeginScrollView(_systemScroll);
            DashboardDiagnostic[] diagnostics = _catalog.Diagnostics
                .Concat(_runtimeDiagnostics.Values)
                .Where(DiagnosticMatchesSearch)
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray();
            int totalDiagnosticCount = _catalog.Diagnostics.Count + _runtimeDiagnostics.Count;

            DrawPageTitle(DashboardText.System, DashboardText.SystemSubtitle);
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
            {
                if (totalDiagnosticCount == 0)
                {
                    DrawStatusLabel(DashboardText.Healthy, SuccessColor);
                    GUILayout.Label(DashboardText.HealthyDescription, EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    DrawStatusLabel(DashboardText.IssuesRequireAttention(totalDiagnosticCount), WarningColor);
                }
                EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                using (new EditorGUILayout.HorizontalScope())
                {
                    int actionCount = _catalog.VisibleModules.Sum(module => module.VisibleActions.Count);
                    int panelCount = _catalog.VisibleWorkspaceModules.Sum(module => module.Panels.Count);
                    DrawInlineMetric(DashboardText.ModuleCount(_catalog.VisibleModules.Count), AccentColor);
                    DrawInlineMetric(DashboardText.ToolCount(actionCount), SuccessColor);
                    DrawInlineMetric(DashboardText.PanelCount(panelCount), AccentColor);
                }
            }

            foreach (DashboardDiagnostic diagnostic in diagnostics)
            {
                MessageType type = diagnostic.Severity == DashboardDiagnosticSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning;
                string title = "[" + DiagnosticSeverityLabel(diagnostic.Severity) + "] " + diagnostic.Code;
                EditorGUILayout.HelpBox(title + "\n" + diagnostic.Message, type);
                if (!string.IsNullOrEmpty(diagnostic.SourcePath))
                    EditorGUILayout.SelectableLabel(diagnostic.SourcePath, EditorStyles.miniLabel, GUILayout.Height(16));
                if (!string.IsNullOrEmpty(diagnostic.MenuPath))
                    EditorGUILayout.SelectableLabel(diagnostic.MenuPath, EditorStyles.miniLabel, GUILayout.Height(16));
            }

            InstalledPackageView[] packages = _installedPackageViews
                .Where(InstalledPackageMatchesSearch)
                .ToArray();
            _showInstalledPackages = EditorUiGUILayout.Disclosure(
                _showInstalledPackages,
                new GUIContent(
                    DashboardText.InstalledPackages(packages.Length),
                    DashboardText.InstalledPackagesTooltip));
            if (_showInstalledPackages)
            {
                using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
                {
                    InstalledPackageView[] issuePackages = packages.Where(item => item.HasDescriptorError).ToArray();
                    InstalledPackageView[] connectedPackages = packages
                        .Where(item => !item.HasDescriptorError && item.Module != null)
                        .ToArray();
                    InstalledPackageView[] packagesWithoutEntry = packages
                        .Where(item => !item.HasDescriptorError && item.Module == null)
                        .ToArray();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawInlineMetric(DashboardText.InstalledCount(packages.Length), AccentColor);
                        DrawInlineMetric(DashboardText.ConnectedPackageCount(connectedPackages.Length), SuccessColor);
                        DrawInlineMetric(
                            DashboardText.PackageWithoutEntryCount(packagesWithoutEntry.Length),
                            EditorUiPalette.Current.MutedText);
                    }
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                    DrawInstalledPackageGroup(
                        issuePackages,
                        ref _showPackageIssues,
                        DashboardText.PackageIssues(issuePackages.Length),
                        DashboardText.PackageIssuesTooltip);
                    DrawInstalledPackageGroup(
                        connectedPackages,
                        ref _showConnectedPackages,
                        DashboardText.ConnectedPackages(connectedPackages.Length),
                        DashboardText.ConnectedPackagesTooltip);
                    DrawInstalledPackageGroup(
                        packagesWithoutEntry,
                        ref _showPackagesWithoutWorkspaceEntry,
                        DashboardText.PackagesWithoutWorkspaceEntry(packagesWithoutEntry.Length),
                        DashboardText.PackagesWithoutWorkspaceEntryTooltip);
                }
            }

            DashboardModule[] projectModules = _catalog.Modules
                .Where(module => module.Source.Kind == DashboardSourceKind.Project)
                .Where(ModuleMatchesSearch)
                .ToArray();
            _showProjectAdapters = EditorUiGUILayout.Disclosure(
                _showProjectAdapters,
                new GUIContent(
                    DashboardText.ProjectAdapters(projectModules.Length),
                    DashboardText.ProjectAdaptersTooltip));
            if (_showProjectAdapters)
            {
                using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
                {
                    foreach (DashboardModule module in projectModules)
                    {
                        EditorUiGUILayout.ActionRow(
                            new GUIContent(module.DisplayName, module.Description),
                            new GUIContent(
                                DashboardText.ContributedTools(module.OwnedVisibleEntries.Count(entry => !entry.IsReference)),
                                module.Description));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private InstalledPackageView CreateInstalledPackageView(DashboardInstalledPackage package)
        {
            DashboardModule module = _catalog.Modules.FirstOrDefault(item =>
                item.Source.Kind == DashboardSourceKind.Package &&
                string.Equals(item.Source.PackageName, package.Name, StringComparison.Ordinal));
            bool hasDescriptorError = _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath));
            return new InstalledPackageView(package, module, hasDescriptorError);
        }

        private void DrawInstalledPackageGroup(
            IReadOnlyList<InstalledPackageView> packages,
            ref bool expanded,
            string label,
            string tooltip)
        {
            if (packages.Count == 0)
                return;
            bool searchActive = !string.IsNullOrEmpty(_search);
            bool visibleExpanded = searchActive || expanded;
            bool nextExpanded = EditorUiGUILayout.Disclosure(
                visibleExpanded,
                new GUIContent(label, searchActive ? DashboardText.PackageSearchExpandedTooltip(tooltip) : tooltip));
            if (!searchActive)
            {
                expanded = nextExpanded;
                visibleExpanded = nextExpanded;
            }
            if (!visibleExpanded)
                return;

            for (int index = 0; index < packages.Count; index++)
            {
                DrawInstalledPackage(packages[index]);
                if (index < packages.Count - 1)
                    EditorUiGUILayout.AccentLine(EditorUiPalette.Current.Border, 1f);
            }
            EditorGUILayout.Space(EditorUiTokens.SpaceXs);
        }

        private static void DrawInstalledPackage(InstalledPackageView view)
        {
            DashboardInstalledPackage package = view.Package;
            DashboardModule module = view.Module;
            string status = view.HasDescriptorError
                ? DashboardText.InstalledDescriptorIssue
                : module != null
                    ? DashboardText.InstalledWorkspaceContent(
                        view.ToolCount,
                        view.PanelCount,
                        view.ReferenceCount)
                    : DashboardText.InstalledWithoutWorkspaceEntry;
            string statusTooltip = view.HasDescriptorError
                ? DashboardText.InstalledDescriptorIssueTooltip
                : module != null
                    ? DashboardText.InstalledWorkspaceContentTooltip
                    : DashboardText.InstalledWithoutWorkspaceEntryTooltip;
            Color statusColor = view.HasDescriptorError
                ? WarningColor
                : module != null
                    ? SuccessColor
                    : EditorUiPalette.Current.MutedText;
            string packageTooltip = DashboardText.InstalledPackageTooltip(package.Name, package.ResolvedPath);
            string versionLabel = DashboardText.PackageVersion(package.Version);
            var versionContent = new GUIContent(versionLabel, DashboardText.PackageVersionTooltip(package.Version));

            using (new EditorGUILayout.VerticalScope(EditorUiStyles.ActionRow))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    GUILayout.Label(new GUIContent(package.DisplayName, packageTooltip), EditorStyles.boldLabel);
                    if (!string.Equals(package.DisplayName, package.Name, StringComparison.Ordinal))
                        GUILayout.Label(new GUIContent(package.Name, packageTooltip), EditorStyles.miniLabel);
                    DrawStatusLabel(new GUIContent(status, statusTooltip), statusColor);
                }
                GUILayout.Space(EditorUiTokens.SpaceMd);
                float versionWidth = Mathf.Clamp(EditorUiStyles.Chip.CalcSize(versionContent).x + 4f, 56f, 112f);
                EditorUiGUILayout.Chip(versionContent, GUILayout.Width(versionWidth));
            }
        }

        private static void DrawStatusLabel(GUIContent content, Color color, params GUILayoutOption[] options)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(content, EditorStyles.miniBoldLabel, options);
            GUI.contentColor = previous;
        }

        private sealed class InstalledPackageView
        {
            internal InstalledPackageView(
                DashboardInstalledPackage package,
                DashboardModule module,
                bool hasDescriptorError)
            {
                Package = package;
                Module = module;
                HasDescriptorError = hasDescriptorError;
                ToolCount = module?.VisibleActions.Count ?? 0;
                PanelCount = module?.Panels.Count ?? 0;
                ReferenceCount = module?.VisibleReferences.Count ?? 0;
            }

            internal DashboardInstalledPackage Package { get; }
            internal DashboardModule Module { get; }
            internal bool HasDescriptorError { get; }
            internal int ToolCount { get; }
            internal int PanelCount { get; }
            internal int ReferenceCount { get; }
        }

        private bool ShouldShowInstalledPackage(DashboardInstalledPackage package)
        {
            if (package.Name.StartsWith("com.zerogamestudio.zeroengine", StringComparison.Ordinal) ||
                string.Equals(package.Name, "com.zerogamestudio.analytics", StringComparison.Ordinal))
            {
                return true;
            }

            if (_catalog.Modules.Any(module =>
                    module.Source.Kind == DashboardSourceKind.Package &&
                    string.Equals(module.Source.PackageName, package.Name, StringComparison.Ordinal)))
            {
                return true;
            }

            return _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath));
        }

        private bool InstalledPackageMatchesSearch(InstalledPackageView view)
        {
            if (string.IsNullOrEmpty(_search))
                return true;
            DashboardInstalledPackage package = view.Package;
            DashboardModule module = view.Module;
            return Matches(package.Name) ||
                   Matches(package.DisplayName) ||
                   Matches(package.Version) ||
                   (module != null && ModuleMatchesSearch(module)) ||
                   _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath) && DiagnosticMatchesSearch(item));
        }

        private bool ModuleMatchesSearch(DashboardModule module)
        {
            return ModuleTextMatchesSearch(module) || module.VisibleSurfaces.Any(SurfaceMatchesSearch);
        }

        private bool ModuleTextMatchesSearch(DashboardModule module)
        {
            return Matches(module.DisplayName) || Matches(module.Description) || Matches(module.ModuleId) ||
                   Matches(module.ProjectId) || Matches(module.ProjectDisplayName);
        }

        private bool EntryMatchesSearch(DashboardEntry entry)
        {
            return Matches(entry.DisplayName) ||
                   Matches(entry.Description) ||
                   Matches(entry.Usage) ||
                   Matches(entry.Category) ||
                   Matches(entry.Section) ||
                   Matches(entry.SurfaceDisplayName) ||
                   Matches(entry.SurfaceActionLabel) ||
                   Matches(entry.MenuPath) ||
                   Matches(entry.ProviderId) ||
                   Matches(entry.ActionId) ||
                   Matches(entry.DocumentationPath) ||
                   Matches(entry.DocumentationUrl) ||
                   (entry.IsReference && Matches(DashboardText.ReferenceResults)) ||
                   entry.LegacyKeywords.Any(Matches) ||
                   Matches(entry.FullId) ||
                   Matches(entry.ModuleId);
        }

        private bool SurfaceMatchesSearch(DashboardSurface surface)
        {
            return Matches(surface.DisplayName) ||
                   Matches(surface.Description) ||
                   Matches(surface.Section) ||
                   surface.Entries.Any(EntryMatchesSearch);
        }

        private string GetMountedOwnerLabel(DashboardEntry entry)
        {
            if (string.Equals(entry.ModuleId, entry.DisplayModuleId, StringComparison.Ordinal))
                return string.Empty;
            DashboardModule owner = _catalog.Modules.FirstOrDefault(module =>
                string.Equals(module.ModuleId, entry.ModuleId, StringComparison.Ordinal));
            return owner == null ? entry.ModuleId : owner.DisplayName;
        }

        private string SurfaceContextLabel(DashboardSurface surface)
        {
            string[] owners = surface.Entries
                .Select(GetMountedOwnerLabel)
                .Where(value => !string.IsNullOrEmpty(value))
                .Select(value => value.StartsWith("POB", StringComparison.OrdinalIgnoreCase) ? "POB" : value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return string.Join(" · ", owners);
        }

        private static void DrawInlineMetric(string text, Color color)
        {
            DrawStatusLabel("●", color, GUILayout.Width(12f));
            GUILayout.Label(text, EditorStyles.miniLabel);
            GUILayout.Space(EditorUiTokens.SpaceSm);
        }

        private void DrawPageTitle(string title, string subtitle)
        {
            EditorUiGUILayout.AccentLine(AccentColor);
            GUILayout.Label(title, EditorUiStyles.SectionTitle);
            GUILayout.Label(subtitle, EditorUiStyles.HeaderSubtitle);
            EditorGUILayout.Space(5f);
        }

        private static void DrawStatusLabel(string text, Color color, params GUILayoutOption[] options)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, EditorStyles.miniBoldLabel, options);
            GUI.contentColor = previous;
        }

        private bool DiagnosticMatchesSearch(DashboardDiagnostic diagnostic)
        {
            return Matches(diagnostic.Code) ||
                   Matches(diagnostic.Message) ||
                   Matches(diagnostic.SourcePath) ||
                   Matches(diagnostic.ModuleId) ||
                   Matches(diagnostic.EntryId) ||
                   Matches(diagnostic.MenuPath);
        }

        private bool Matches(string value)
        {
            return string.IsNullOrEmpty(_search) ||
                   (!string.IsNullOrEmpty(value) && value.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsAvailable(DashboardEntry entry)
        {
            return entry.Availability == DashboardEntryAvailability.Always ||
                   (entry.Availability == DashboardEntryAvailability.EditMode && !EditorApplication.isPlaying) ||
                   (entry.Availability == DashboardEntryAvailability.PlayMode && EditorApplication.isPlaying);
        }

        private static string SafetyLabel(DashboardEntrySafety safety)
        {
            switch (safety)
            {
                case DashboardEntrySafety.ReadOnly: return DashboardText.ReadOnly;
                case DashboardEntrySafety.ProjectWrite: return DashboardText.ProjectWrite;
                case DashboardEntrySafety.Destructive: return DashboardText.Destructive;
                default: return DashboardText.Navigation;
            }
        }

        private static string SafetyTooltip(DashboardEntrySafety safety)
        {
            switch (safety)
            {
                case DashboardEntrySafety.ReadOnly: return DashboardText.ReadOnlyTooltip;
                case DashboardEntrySafety.ProjectWrite: return DashboardText.ProjectWriteTooltip;
                case DashboardEntrySafety.Destructive: return DashboardText.DestructiveTooltip;
                default: return DashboardText.NavigationTooltip;
            }
        }

        private static string SafetyId(DashboardEntrySafety safety)
        {
            switch (safety)
            {
                case DashboardEntrySafety.ReadOnly: return "read-only";
                case DashboardEntrySafety.ProjectWrite: return "project-write";
                case DashboardEntrySafety.Destructive: return "destructive";
                default: return "navigation";
            }
        }

        private static string DiagnosticSeverityLabel(DashboardDiagnosticSeverity severity)
        {
            return severity == DashboardDiagnosticSeverity.Error ? DashboardText.Error : DashboardText.Warning;
        }

        private sealed class SurfaceView
        {
            internal SurfaceView(
                DashboardModule module,
                DashboardSurface surface,
                IReadOnlyList<DashboardEntry> entries,
                DashboardEntry defaultEntry)
            {
                Module = module;
                Surface = surface;
                Entries = entries;
                DefaultEntry = defaultEntry;
            }

            internal DashboardModule Module { get; }
            internal DashboardSurface Surface { get; }
            internal IReadOnlyList<DashboardEntry> Entries { get; }
            internal DashboardEntry DefaultEntry { get; }
        }

        private sealed class WorkspacePanelView
        {
            internal WorkspacePanelView(DashboardModule module, DashboardPanel panel)
            {
                Module = module;
                Panel = panel;
            }

            internal DashboardModule Module { get; }
            internal DashboardPanel Panel { get; }
        }

        private sealed class WorkspaceModuleView
        {
            internal WorkspaceModuleView(DashboardModule module, WorkspacePanelView[] panels)
            {
                Module = module;
                Panels = panels ?? Array.Empty<WorkspacePanelView>();
            }

            internal DashboardModule Module { get; }
            internal WorkspacePanelView[] Panels { get; }
        }

        private sealed class ReferenceView
        {
            internal ReferenceView(DashboardModule module, DashboardEntry entry)
            {
                Module = module;
                Entry = entry;
            }

            internal DashboardModule Module { get; }
            internal DashboardEntry Entry { get; }
        }

        private readonly struct ActionUiState
        {
            internal ActionUiState(bool enabled, bool isChecked, string disabledReason)
            {
                Enabled = enabled;
                IsChecked = isChecked;
                DisabledReason = disabledReason ?? string.Empty;
            }

            internal bool Enabled { get; }
            internal bool IsChecked { get; }
            internal string DisabledReason { get; }
        }

        private static Color AccentColor => EditorUiPalette.Current.Accent;
        private static Color SuccessColor => EditorUiPalette.Current.Success;
        private static Color WarningColor => EditorUiPalette.Current.Warning;
        private static bool IsBelow(string candidate, string root)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
                return false;
            try
            {
                string fullCandidate = Path.GetFullPath(candidate);
                string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                  Path.DirectorySeparatorChar;
                return fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal sealed class DashboardViewState
    {
        internal const float DefaultWorkspaceSidebarWidth = 244f;

        internal int Page;
        internal int HomeView;
        internal string Search = string.Empty;
        internal string SelectedCategoryId = "authoring";
        internal string SelectedScopeId = string.Empty;
        internal string SelectedSafetyId = string.Empty;
        internal string SelectedAvailabilityId = string.Empty;
        internal bool ShowAdvanced = true;
        internal bool ShowMaintenance;
        internal string SelectedPanelFullId = string.Empty;
        internal string[] WorkspaceModuleOrder = Array.Empty<string>();
        internal string[] WorkspacePanelOrder = Array.Empty<string>();
        internal string[] CollapsedWorkspaceModuleIds = Array.Empty<string>();
        internal float WorkspaceSidebarWidth = DefaultWorkspaceSidebarWidth;
        internal Vector2 ModuleScroll;
        internal Vector2 ContentScroll;
        internal Vector2 SystemScroll;
        internal Vector2 WorkspaceNavigationScroll;
        internal Vector2 WorkspaceContentScroll;
        internal Vector2 ContextScroll;
    }

    internal static class DashboardViewStateStore
    {
        private const string DefaultPrefix = "ZGS.Workbench.";
        private const int CurrentNavigationVersion = 1;
        private const int AllToolsHomeView = 1;

        internal static DashboardViewState Load(string prefix = DefaultPrefix)
        {
            prefix = NormalizePrefix(prefix);
            int navigationVersion = EditorPrefs.GetInt(prefix + "NavigationVersion", 0);
            int page = EditorPrefs.GetInt(prefix + "Page", 0);
            int homeView = EditorPrefs.GetInt(prefix + "HomeView", 0);
            if (navigationVersion < CurrentNavigationVersion)
            {
                if (page == 1)
                {
                    page = 0;
                    homeView = AllToolsHomeView;
                }
                else if (page == 2)
                {
                    page = 1;
                }
                else if (page >= 3)
                {
                    page = 2;
                }
            }
            return new DashboardViewState
            {
                Page = page,
                HomeView = homeView,
                Search = EditorPrefs.GetString(prefix + "Search", string.Empty),
                SelectedCategoryId = EditorPrefs.GetString(prefix + "SelectedCategory", "authoring"),
                SelectedScopeId = EditorPrefs.GetString(prefix + "SelectedScope", string.Empty),
                SelectedSafetyId = EditorPrefs.GetString(prefix + "SelectedSafety", string.Empty),
                SelectedAvailabilityId = EditorPrefs.GetString(prefix + "SelectedAvailability", string.Empty),
                ShowAdvanced = EditorPrefs.GetBool(prefix + "ShowAdvanced", true),
                ShowMaintenance = EditorPrefs.GetBool(prefix + "ShowMaintenance", false),
                SelectedPanelFullId = EditorPrefs.GetString(prefix + "SelectedPanel", string.Empty),
                WorkspaceModuleOrder = LoadStringList(prefix + "WorkspaceModuleOrder"),
                WorkspacePanelOrder = LoadStringList(prefix + "WorkspacePanelOrder"),
                CollapsedWorkspaceModuleIds = LoadStringList(prefix + "CollapsedWorkspaceModuleIds"),
                WorkspaceSidebarWidth = EditorPrefs.GetFloat(
                    prefix + "WorkspaceSidebarWidth",
                    DashboardViewState.DefaultWorkspaceSidebarWidth),
                ModuleScroll = LoadVector(prefix + "ModuleScroll"),
                ContentScroll = LoadVector(prefix + "ContentScroll"),
                SystemScroll = LoadVector(prefix + "SystemScroll"),
                WorkspaceNavigationScroll = LoadVector(prefix + "WorkspaceNavigationScroll"),
                WorkspaceContentScroll = LoadVector(prefix + "WorkspaceContentScroll"),
                ContextScroll = LoadVector(prefix + "ContextScroll")
            };
        }

        internal static void Save(DashboardViewState state, string prefix = DefaultPrefix)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            prefix = NormalizePrefix(prefix);
            EditorPrefs.SetInt(prefix + "NavigationVersion", CurrentNavigationVersion);
            EditorPrefs.SetInt(prefix + "Page", state.Page);
            EditorPrefs.SetInt(prefix + "HomeView", state.HomeView);
            EditorPrefs.SetString(prefix + "Search", state.Search ?? string.Empty);
            EditorPrefs.SetString(prefix + "SelectedCategory", state.SelectedCategoryId ?? string.Empty);
            EditorPrefs.SetString(prefix + "SelectedScope", state.SelectedScopeId ?? string.Empty);
            EditorPrefs.SetString(prefix + "SelectedSafety", state.SelectedSafetyId ?? string.Empty);
            EditorPrefs.SetString(prefix + "SelectedAvailability", state.SelectedAvailabilityId ?? string.Empty);
            EditorPrefs.SetBool(prefix + "ShowAdvanced", state.ShowAdvanced);
            EditorPrefs.SetBool(prefix + "ShowMaintenance", state.ShowMaintenance);
            EditorPrefs.SetString(prefix + "SelectedPanel", state.SelectedPanelFullId ?? string.Empty);
            EditorPrefs.SetString(
                prefix + "WorkspaceModuleOrder",
                string.Join("\n", state.WorkspaceModuleOrder ?? Array.Empty<string>()));
            EditorPrefs.SetString(
                prefix + "WorkspacePanelOrder",
                string.Join("\n", state.WorkspacePanelOrder ?? Array.Empty<string>()));
            EditorPrefs.SetString(
                prefix + "CollapsedWorkspaceModuleIds",
                string.Join("\n", state.CollapsedWorkspaceModuleIds ?? Array.Empty<string>()));
            EditorPrefs.SetFloat(prefix + "WorkspaceSidebarWidth", state.WorkspaceSidebarWidth);
            SaveVector(prefix + "ModuleScroll", state.ModuleScroll);
            SaveVector(prefix + "ContentScroll", state.ContentScroll);
            SaveVector(prefix + "SystemScroll", state.SystemScroll);
            SaveVector(prefix + "WorkspaceNavigationScroll", state.WorkspaceNavigationScroll);
            SaveVector(prefix + "WorkspaceContentScroll", state.WorkspaceContentScroll);
            SaveVector(prefix + "ContextScroll", state.ContextScroll);
        }

        internal static void Delete(string prefix)
        {
            prefix = NormalizePrefix(prefix);
            string[] scalarKeys =
            {
                "NavigationVersion", "Page", "HomeView", "Search", "SelectedCategory", "SelectedScope", "SelectedSafety",
                "SelectedAvailability", "ShowAdvanced", "ShowMaintenance", "SelectedPanel",
                "WorkspaceModuleOrder", "WorkspacePanelOrder", "CollapsedWorkspaceModuleIds",
                "WorkspaceSidebarWidth"
            };
            foreach (string key in scalarKeys)
                EditorPrefs.DeleteKey(prefix + key);
            foreach (string key in new[]
                     {
                         "ModuleScroll", "ContentScroll", "SystemScroll", "WorkspaceNavigationScroll",
                         "WorkspaceContentScroll", "ContextScroll"
                     })
            {
                EditorPrefs.DeleteKey(prefix + key + "X");
                EditorPrefs.DeleteKey(prefix + key + "Y");
            }
        }

        private static Vector2 LoadVector(string key)
        {
            return new Vector2(EditorPrefs.GetFloat(key + "X", 0f), EditorPrefs.GetFloat(key + "Y", 0f));
        }

        private static string[] LoadStringList(string key)
        {
            return EditorPrefs.GetString(key, string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void SaveVector(string key, Vector2 value)
        {
            EditorPrefs.SetFloat(key + "X", value.x);
            EditorPrefs.SetFloat(key + "Y", value.y);
        }

        private static string NormalizePrefix(string prefix)
        {
            return string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;
        }
    }

    internal static class DashboardWorkspaceOrder
    {
        internal static string[] Visible(IEnumerable<string> preferredOrder, IEnumerable<string> availableOrder)
        {
            string[] available = Normalize(availableOrder);
            var availableSet = new HashSet<string>(available, StringComparer.Ordinal);
            return Reconcile(preferredOrder, available)
                .Where(availableSet.Contains)
                .ToArray();
        }

        internal static string[] Move(
            IEnumerable<string> preferredOrder,
            IEnumerable<string> availableOrder,
            string draggedId,
            string targetId,
            bool before)
        {
            string[] available = Normalize(availableOrder);
            var availableSet = new HashSet<string>(available, StringComparer.Ordinal);
            List<string> order = Reconcile(preferredOrder, available).ToList();
            if (string.IsNullOrEmpty(draggedId) ||
                string.IsNullOrEmpty(targetId) ||
                string.Equals(draggedId, targetId, StringComparison.Ordinal) ||
                !availableSet.Contains(draggedId) ||
                !availableSet.Contains(targetId))
            {
                return order.ToArray();
            }

            order.Remove(draggedId);
            int targetIndex = order.IndexOf(targetId);
            order.Insert(before ? targetIndex : targetIndex + 1, draggedId);
            return order.ToArray();
        }

        private static string[] Reconcile(IEnumerable<string> preferredOrder, IEnumerable<string> availableOrder)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in Normalize(preferredOrder))
            {
                if (seen.Add(id))
                    result.Add(id);
            }
            foreach (string id in Normalize(availableOrder))
            {
                if (seen.Add(id))
                    result.Add(id);
            }
            return result.ToArray();
        }

        private static string[] Normalize(IEnumerable<string> ids)
        {
            return (ids ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }

    internal readonly struct DashboardWorkspacePanelLayout
    {
        internal DashboardWorkspacePanelLayout(Rect handleRect, Rect buttonRect, Rect selectionRect)
        {
            HandleRect = handleRect;
            ButtonRect = buttonRect;
            SelectionRect = selectionRect;
        }

        internal Rect HandleRect { get; }
        internal Rect ButtonRect { get; }
        internal Rect SelectionRect { get; }
    }

    internal readonly struct DashboardWorkspaceSplitLayout
    {
        internal DashboardWorkspaceSplitLayout(float sidebarWidth, float contentWidth)
        {
            SidebarWidth = sidebarWidth;
            ContentWidth = contentWidth;
        }

        internal float SidebarWidth { get; }
        internal float ContentWidth { get; }
    }

    internal static class DashboardWorkspaceLayout
    {
        internal static DashboardWorkspaceSplitLayout CalculateSplitLayout(
            float windowWidth,
            float preferredSidebarWidth,
            float chromeWidth,
            float sidebarMinWidth,
            float sidebarMaxWidth,
            float sidebarMaximumFraction)
        {
            float availableWidth = Mathf.Max(2f, windowWidth - Mathf.Max(0f, chromeWidth));
            float sidebarLimit = Mathf.Min(
                Mathf.Max(1f, sidebarMaxWidth),
                availableWidth * Mathf.Clamp(sidebarMaximumFraction, 0.1f, 0.9f));
            float responsiveMinimum = Mathf.Min(Mathf.Max(1f, sidebarMinWidth), sidebarLimit);
            float sidebarWidth = Mathf.Clamp(preferredSidebarWidth, responsiveMinimum, sidebarLimit);
            return new DashboardWorkspaceSplitLayout(
                sidebarWidth,
                Mathf.Max(1f, availableWidth - sidebarWidth));
        }

        internal static DashboardWorkspacePanelLayout CalculatePanelLayout(
            Rect rowRect,
            float handleInset,
            float handleWidth,
            float gap,
            float rightInset,
            float verticalInset,
            float selectionWidth)
        {
            float safeHandleInset = Mathf.Max(0f, handleInset);
            float safeHandleWidth = Mathf.Max(0f, handleWidth);
            float safeGap = Mathf.Max(0f, gap);
            float safeRightInset = Mathf.Max(0f, rightInset);
            float safeVerticalInset = Mathf.Clamp(verticalInset, 0f, Mathf.Max(0f, (rowRect.height - 1f) * 0.5f));
            float buttonRight = Mathf.Max(rowRect.x + 1f, rowRect.xMax - safeRightInset);
            float buttonX = Mathf.Min(
                rowRect.x + safeHandleInset + safeHandleWidth + safeGap,
                buttonRight - 1f);
            float handleX = Mathf.Min(rowRect.x + safeHandleInset, buttonX);
            float actualHandleWidth = Mathf.Min(safeHandleWidth, Mathf.Max(0f, buttonX - safeGap - handleX));
            Rect handleRect = new Rect(
                handleX,
                rowRect.y,
                actualHandleWidth,
                rowRect.height);
            Rect buttonRect = new Rect(
                buttonX,
                rowRect.y + safeVerticalInset,
                buttonRight - buttonX,
                Mathf.Max(1f, rowRect.height - safeVerticalInset * 2f));
            float safeSelectionWidth = Mathf.Min(Mathf.Max(1f, selectionWidth), buttonRect.width);
            Rect selectionRect = new Rect(
                buttonRect.x,
                buttonRect.y + 1f,
                safeSelectionWidth,
                Mathf.Max(1f, buttonRect.height - 2f));
            return new DashboardWorkspacePanelLayout(handleRect, buttonRect, selectionRect);
        }
    }

    internal static class DashboardWorkspaceFoldout
    {
        internal static bool IsExpanded(IEnumerable<string> collapsedModuleIds, string moduleId, bool searchActive)
        {
            return searchActive || !(collapsedModuleIds ?? Array.Empty<string>())
                .Contains(moduleId, StringComparer.Ordinal);
        }

        internal static void SetAll(
            ISet<string> collapsedModuleIds,
            IEnumerable<string> availableModuleIds,
            bool expanded)
        {
            if (collapsedModuleIds == null)
                return;
            foreach (string moduleId in (availableModuleIds ?? Array.Empty<string>())
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal))
            {
                if (expanded)
                    collapsedModuleIds.Remove(moduleId);
                else
                    collapsedModuleIds.Add(moduleId);
            }
        }
    }
}
