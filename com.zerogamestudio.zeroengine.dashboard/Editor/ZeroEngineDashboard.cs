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
    public sealed class ZeroEngineDashboard : EditorWindow
    {
        private static readonly GUIContent[] PageNames =
        {
            new GUIContent(DashboardText.Tools, DashboardText.ToolsTooltip),
            new GUIContent(DashboardText.Workspace, DashboardText.WorkspaceTooltip),
            new GUIContent(DashboardText.System, DashboardText.SystemTooltip)
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
            new GUIContent("内容创作", "数据编辑器、公式、任务、工坊和内容预览。"),
            new GUIContent("数据与本地化", "配置、Schema、检索、导入导出和本地化工具。"),
            new GUIContent("资源与构建", "Addressables、纹理、字体、音频、Shader 与构建准备。"),
            new GUIContent("检查与调试", "只读审计、运行诊断、可视化调试和健康报告。"),
            new GUIContent("测试与发布", "具名窄范围测试、发布检查和可审计打包流程。"),
            new GUIContent("系统与安装", "通用包安装、系统工具和框架级维护。")
        };

        private readonly Dictionary<string, DashboardDiagnostic> _runtimeDiagnostics =
            new Dictionary<string, DashboardDiagnostic>(StringComparer.Ordinal);

        private DashboardCatalog _catalog = DashboardCatalog.Empty;
        private int _page;
        private string _search = string.Empty;
        private string _selectedModuleId = string.Empty;
        private string _selectedCategoryId = "authoring";
        private string _selectedScopeId = string.Empty;
        private bool _showAdvanced;
        private bool _showMaintenance;
        private bool _focusSearch;
        private Vector2 _moduleScroll;
        private Vector2 _contentScroll;
        private Vector2 _systemScroll;
        private Vector2 _workspaceNavigationScroll;
        private Vector2 _workspaceContentScroll;
        private bool _showInstalledPackages;
        private bool _showProjectAdapters;
        private bool _showHelp;
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
        private readonly HashSet<string> _failedWorkspacePanels = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedDetails = new HashSet<string>(StringComparer.Ordinal);

        [MenuItem("ZGS/工作台")]
        public static void ShowWindow()
        {
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>(DashboardText.WindowTitle);
            window.titleContent = new GUIContent(DashboardText.WindowTitle, DashboardText.ToolsTooltip);
            window.minSize = new Vector2(760f, 460f);
            window._page = 0;
            window._focusSearch = true;
            window.Show();
            window.Focus();
        }

        public static void ShowWorkspace(string moduleId, string panelId)
        {
            ShowWindow();
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>(DashboardText.WindowTitle);
            window._page = 1;
            window._selectedPanelFullId = (moduleId ?? string.Empty) + "/" + (panelId ?? string.Empty);
            window.Show();
            window.Focus();
            window.Repaint();
        }

        private void OnEnable()
        {
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged += RefreshCatalog;
            EditorApplication.update += OnEditorUpdate;
            minSize = new Vector2(760f, 460f);
            RefreshCatalog();
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged -= RefreshCatalog;
            EditorApplication.update -= OnEditorUpdate;
            DeactivateWorkspacePanel();
            _actionRegistry = null;
        }

        private void OnRegisteredPackages(PackageRegistrationEventArgs eventArgs)
        {
            RefreshCatalog();
        }

        private void RefreshCatalog()
        {
            DeactivateWorkspacePanel();
            try
            {
                _catalog = DashboardCatalogDiscovery.Discover();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _catalog = new DashboardCatalog(
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

            if (!string.IsNullOrEmpty(_selectedModuleId) &&
                _catalog.VisibleModules.All(module => !string.Equals(
                    module.ModuleId,
                    _selectedModuleId,
                    StringComparison.Ordinal)))
            {
                _selectedModuleId = string.Empty;
            }

            if (!string.IsNullOrEmpty(_selectedPanelFullId) &&
                !_catalog.VisibleWorkspaceModules.SelectMany(module => module.Panels)
                    .Any(panel => string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal)))
            {
                _selectedPanelFullId = string.Empty;
                DeactivateWorkspacePanel();
            }
            Repaint();
        }

        private void OnEditorUpdate()
        {
            if (_page != 1 || _activePanel == null || _activePanelContext == null)
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
            DrawHelpDrawer();
            switch (_page)
            {
                case 0:
                    DeactivateWorkspacePanel();
                    DrawTools();
                    break;
                case 1:
                    DrawWorkspace();
                    break;
                default:
                    DeactivateWorkspacePanel();
                    DrawSystem();
                    break;
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
                    int toolCount = _catalog.VisibleModules.Sum(module => module.VisibleEntries.Count);
                    int panelCount = _catalog.VisibleWorkspaceModules.Sum(module => module.Panels.Count);
                    if (!compact)
                    {
                        DrawInlineMetric(DashboardText.ModuleCount(_catalog.VisibleModules.Count), AccentColor);
                        DrawInlineMetric(DashboardText.ToolCount(toolCount), SuccessColor);
                        DrawInlineMetric(DashboardText.PanelCount(panelCount), AccentColor);
                    }
                    DrawInlineMetric(DashboardText.IssueCount(diagnosticCount), diagnosticCount == 0 ? SuccessColor : WarningColor);
                    if (GUILayout.Button(
                            EditorGUIUtility.IconContent("Refresh", DashboardText.RefreshTooltip),
                            GUILayout.Width(30f),
                            GUILayout.Height(24f)))
                    {
                        RefreshCatalog();
                    }
                    if (GUILayout.Button(
                            new GUIContent("?", DashboardText.HelpTooltip),
                            GUILayout.Width(30f),
                            GUILayout.Height(24f)))
                    {
                        _showHelp = !_showHelp;
                    }
                });
        }

        private void DrawNavigation()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                _page = GUILayout.Toolbar(_page, PageNames, GUILayout.Width(180f), GUILayout.Height(24f));

                GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                       GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                       EditorStyles.textField;
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
            EditorGUILayout.Space(6f);
        }

        private void DrawSearchField(GUIStyle searchStyle)
        {
            GUI.SetNextControlName("ZGS.Workbench.Search");
            Rect rect = GUILayoutUtility.GetRect(
                140f,
                22f,
                searchStyle,
                GUILayout.MinWidth(140f),
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

        private void DrawTools()
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

            if (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact)
            {
                DrawCompactCategorySelector(modules);
                DrawToolContent(modules);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCategoryList(modules);
                EditorGUILayout.Space(8f);
                DrawToolContent(modules);
            }
        }

        private void DrawToolFilters(IReadOnlyList<DashboardModule> modules)
        {
            var scopeIds = new List<string> { string.Empty, "universal" };
            var scopeLabels = new List<GUIContent>
            {
                new GUIContent("全部", "显示通用模块和所有项目适配器。"),
                new GUIContent("通用", "只显示通用 ZeroEngine 模块。")
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
                    "只显示 " + module.ProjectDisplayName + " 项目适配器。"));
            }

            int scopeIndex = scopeIds.FindIndex(id => string.Equals(id, _selectedScopeId, StringComparison.Ordinal));
            if (scopeIndex < 0)
                scopeIndex = 0;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(new GUIContent("范围", "按 descriptor 声明的通用或项目范围筛选。"), GUILayout.Width(32f));
                int selected = EditorGUILayout.Popup(scopeIndex, scopeLabels.ToArray(), GUILayout.Width(120f));
                _selectedScopeId = scopeIds[Mathf.Clamp(selected, 0, scopeIds.Count - 1)];
                GUILayout.Space(EditorUiTokens.SpaceSm);
                _showAdvanced = GUILayout.Toggle(
                    _showAdvanced,
                    new GUIContent("高级工具", "显示专业检查、构建和项目写入工具。"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(80f));
                _showMaintenance = GUILayout.Toggle(
                    _showMaintenance,
                    new GUIContent("维护工具", "显示恢复、迁移和高影响工具。"),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(80f));
                GUILayout.FlexibleSpace();
            }
            if (_showMaintenance)
                EditorGUILayout.HelpBox("维护工具可能修改或破坏项目数据；请逐项阅读安全提示和确认内容。", MessageType.Warning);
        }

        private void DrawCategoryList(IReadOnlyList<DashboardModule> modules)
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.Width(EditorUiTokens.DashboardSidebarWidth)))
            {
                GUILayout.Label("任务分类", EditorStyles.boldLabel);
                _moduleScroll = EditorGUILayout.BeginScrollView(_moduleScroll);
                for (int index = 0; index < ToolCategoryIds.Length; index++)
                {
                    string categoryId = ToolCategoryIds[index];
                    GUIContent category = ToolCategoryNames[index];
                    if (DrawSelectionButton(
                            category.text,
                            category.tooltip,
                            CountVisibleEntries(modules, categoryId),
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
            var labels = new List<GUIContent>();
            for (int index = 0; index < ToolCategoryIds.Length; index++)
            {
                GUIContent category = ToolCategoryNames[index];
                labels.Add(new GUIContent(
                    category.text + "（" + CountVisibleEntries(modules, ToolCategoryIds[index]) + "）",
                    category.tooltip));
            }

            int currentIndex = Math.Max(0, Array.IndexOf(ToolCategoryIds, _selectedCategoryId));
            int selected = EditorGUILayout.Popup(
                new GUIContent("任务分类", "选择当前要浏览的工具分类。"),
                currentIndex,
                labels.ToArray());
            _selectedCategoryId = ToolCategoryIds[Mathf.Clamp(selected, 0, ToolCategoryIds.Length - 1)];
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
            IEnumerable<DashboardModule> selectedModules = modules.Where(ModuleMatchesScope);

            int drawnSurfaces = 0;
            foreach (DashboardModule module in selectedModules)
            {
                DashboardSurface[] surfaces = module.VisibleSurfaces
                    .Where(surface => SurfaceMatchesCategoryAndVisibility(surface) &&
                                      (SurfaceMatchesSearch(surface) || ModuleTextMatchesSearch(module)))
                    .ToArray();
                if (surfaces.Length == 0 && !ModuleTextMatchesSearch(module))
                    continue;

                DrawModuleHeader(module);
                foreach (IGrouping<string, DashboardSurface> section in surfaces.GroupBy(surface => surface.Section, StringComparer.Ordinal))
                {
                    GUILayout.Label(section.Key, EditorStyles.miniBoldLabel);
                    using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
                    {
                        DashboardSurface[] sectionSurfaces = section.ToArray();
                        for (int index = 0; index < sectionSurfaces.Length; index++)
                        {
                            DrawSurface(module, sectionSurfaces[index]);
                            if (index < sectionSurfaces.Length - 1)
                                EditorUiGUILayout.AccentLine(EditorUiPalette.Current.Border, 1f);
                            drawnSurfaces++;
                        }
                    }
                }
                EditorGUILayout.Space(EditorUiTokens.SpaceSm);
            }

            if (drawnSurfaces == 0)
                EditorGUILayout.HelpBox(DashboardText.NoSearchResults, MessageType.Info);
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
            foreach (DashboardModule module in modules)
            {
                foreach (DashboardEntry entry in module.VisibleEntries.Where(EntryMatchesSearch))
                {
                    bool scopeVisible = ModuleMatchesScope(module);
                    bool categoryVisible = string.Equals(EffectiveCategory(entry), _selectedCategoryId, StringComparison.Ordinal);
                    bool visibilityVisible = EntryMatchesVisibility(entry);
                    if (scopeVisible && categoryVisible && visibilityVisible)
                        continue;
                    hidden.Add(entry);
                    needsCategoryOrScope |= !scopeVisible || !categoryVisible;
                    needsAdvanced |= entry.Visibility == DashboardEntryVisibility.Advanced && !_showAdvanced;
                    needsMaintenance |= entry.Visibility == DashboardEntryVisibility.Maintenance && !_showMaintenance;
                }
            }

            int count = hidden.Select(entry => entry.FullId).Distinct(StringComparer.Ordinal).Count();
            if (count == 0)
                return;
            var actions = new List<string>();
            if (needsCategoryOrScope) actions.Add("切换任务分类或范围");
            if (needsAdvanced) actions.Add("开启高级工具");
            if (needsMaintenance) actions.Add("开启维护工具");
            EditorGUILayout.HelpBox(
                "另有 " + count + " 个匹配项被筛选隐藏；请" + string.Join("、", actions) + "。",
                MessageType.Info);
        }

        private int CountVisibleEntries(IEnumerable<DashboardModule> modules, string categoryId)
        {
            return modules.Where(ModuleMatchesScope)
                .SelectMany(module => module.VisibleEntries)
                .Count(entry => string.Equals(EffectiveCategory(entry), categoryId, StringComparison.Ordinal) &&
                                EntryMatchesVisibility(entry));
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

        private bool SurfaceMatchesCategoryAndVisibility(DashboardSurface surface)
        {
            return surface.Entries.Any(entry =>
                string.Equals(EffectiveCategory(entry), _selectedCategoryId, StringComparison.Ordinal) &&
                EntryMatchesVisibility(entry));
        }

        private bool EntryMatchesVisibility(DashboardEntry entry)
        {
            return entry.Visibility == DashboardEntryVisibility.Primary ||
                   (entry.Visibility == DashboardEntryVisibility.Advanced && _showAdvanced) ||
                   (entry.Visibility == DashboardEntryVisibility.Maintenance && _showMaintenance);
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

        private void DrawModuleHeader(DashboardModule module)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(module.DisplayName, EditorUiStyles.SectionTitle);
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(DashboardText.ToolCount(module.VisibleEntries.Count), EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("?", DashboardText.HelpTooltip), GUILayout.Width(28f)))
                    ShowHelp(module, null, null);
            }
            EditorUiGUILayout.AccentLine(AccentColor);
            EditorGUILayout.Space(EditorUiTokens.SpaceXs);
        }

        private void DrawSurface(DashboardModule module, DashboardSurface surface)
        {
            DashboardEntry[] visibleEntries = surface.Entries
                .Where(entry => string.Equals(EffectiveCategory(entry), _selectedCategoryId, StringComparison.Ordinal) &&
                                EntryMatchesVisibility(entry))
                .ToArray();
            DashboardEntry visibleDefault = visibleEntries.Contains(surface.DefaultEntry)
                ? surface.DefaultEntry
                : visibleEntries[0];
            float trailingWidth = CalculateSurfaceActionWidth(visibleEntries) + 34f;
            float availableWidth = Mathf.Max(240f, position.width -
                (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact
                    ? 56f
                    : EditorUiTokens.DashboardSidebarWidth + 92f));
            EditorUiActionRowMode rowMode = EditorUiGUILayout.ResolveActionRowMode(availableWidth, trailingWidth);
            EditorUiGUILayout.ActionRow(
                new GUIContent(surface.DisplayName, surface.Description),
                null,
                () =>
                {
                    DrawSurfaceActions(visibleEntries, visibleDefault);
                    if (GUILayout.Button(new GUIContent("?", DashboardText.HelpTooltip), GUILayout.Width(28f), GUILayout.Height(EditorUiTokens.PrimaryButtonHeight)))
                        ShowHelp(module, surface, null);
                },
                rowMode);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorUiTokens.SpaceSm);
                string context = SurfaceContextLabel(surface);
                if (!string.IsNullOrEmpty(context))
                    EditorUiGUILayout.Chip(context);
                if (visibleEntries.Any(entry => entry.IsLegacy))
                    EditorUiGUILayout.Chip(new GUIContent("旧版入口", "此入口仍使用 schema v1，仅在 Dashboard 4.x 兼容。"));
                GUILayout.FlexibleSpace();

                string detailKey = module.ModuleId + "/" + surface.SurfaceId;
                bool expanded = _expandedDetails.Contains(detailKey);
                bool next = EditorUiGUILayout.Disclosure(
                    expanded,
                    new GUIContent(DashboardText.Details, DashboardText.DetailsTooltip));
                if (next != expanded)
                {
                    if (next) _expandedDetails.Add(detailKey);
                    else _expandedDetails.Remove(detailKey);
                }
            }

            string currentKey = module.ModuleId + "/" + surface.SurfaceId;
            if (_expandedDetails.Contains(currentKey))
            {
                EditorGUILayout.SelectableLabel(module.ModuleId, EditorStyles.miniLabel, GUILayout.Height(16f));
                foreach (DashboardEntry entry in visibleEntries)
                {
                    EditorGUILayout.SelectableLabel(
                        EntryTechnicalRoute(entry),
                        EditorStyles.miniLabel,
                        GUILayout.Height(16f));
                }
            }

            foreach (DashboardEntry entry in visibleEntries)
            {
                if (!IsAvailable(entry))
                {
                    EditorGUILayout.HelpBox(
                        entry.Availability == DashboardEntryAvailability.EditMode
                            ? DashboardText.EditModeOnly(entry.DisplayName)
                            : DashboardText.PlayModeOnly(entry.DisplayName),
                        MessageType.Info);
                }
                DashboardDiagnostic diagnostic = FindActionDiagnostic(entry);
                if (diagnostic != null)
                {
                    EditorGUILayout.HelpBox(entry.DisplayName + ": " + diagnostic.Message, MessageType.Error);
                    continue;
                }
                if (entry.ExecutionKind != DashboardEntryExecutionKind.Provider || _actionRegistry == null)
                    continue;
                if (_actionRegistry.TryGetState(entry, out EditorToolActionState state, out DashboardDiagnostic stateDiagnostic))
                {
                    if (!state.Enabled)
                        EditorGUILayout.HelpBox(entry.DisplayName + "：" + state.DisabledReason, MessageType.Info);
                }
                else
                {
                    RecordActionDiagnostic(entry, stateDiagnostic);
                }
            }
        }

        private void DrawSurfaceActions(IReadOnlyList<DashboardEntry> entries, DashboardEntry defaultEntry)
        {
            foreach (DashboardEntry entry in entries)
            {
                bool available = IsAvailable(entry);
                bool failed = FindActionDiagnostic(entry) != null;
                bool isChecked = false;
                string disabledReason = string.Empty;
                if (entry.ExecutionKind == DashboardEntryExecutionKind.Provider && _actionRegistry != null)
                {
                    if (_actionRegistry.TryGetState(entry, out EditorToolActionState state, out DashboardDiagnostic diagnostic))
                    {
                        available &= state.Enabled;
                        isChecked = state.IsChecked;
                        disabledReason = state.DisabledReason;
                    }
                    else
                    {
                        failed = true;
                        RecordActionDiagnostic(entry, diagnostic);
                    }
                }

                string label = ActionLabel(entry);
                if (isChecked)
                    label = "✓ " + label;
                string tooltip = string.IsNullOrEmpty(disabledReason)
                    ? entry.Description
                    : entry.Description + "\n" + disabledReason;
                var content = new GUIContent(label, tooltip);
                using (new EditorGUI.DisabledScope(!available || failed))
                {
                    bool clicked = entry == defaultEntry
                        ? EditorUiGUILayout.PrimaryButton(content, GUILayout.MinWidth(72f))
                        : GUILayout.Button(content, GUILayout.MinWidth(72f), GUILayout.Height(EditorUiTokens.PrimaryButtonHeight));
                    if (clicked)
                        ExecuteEntry(entry);
                }
            }
        }

        private static float CalculateSurfaceActionWidth(IEnumerable<DashboardEntry> entries)
        {
            float width = 0f;
            foreach (DashboardEntry entry in entries)
            {
                string label = ActionLabel(entry);
                width += Mathf.Max(72f, GUI.skin.button.CalcSize(new GUIContent(label)).x + 12f);
                width += EditorUiTokens.SpaceXs;
            }
            return width;
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
            string label = string.IsNullOrEmpty(entry.SurfaceActionLabel)
                ? entry.Kind == DashboardEntryKind.Window ? DashboardText.Open : DashboardText.Run
                : entry.SurfaceActionLabel;
            return entry.Safety == DashboardEntrySafety.Navigation
                ? label
                : label + " · " + SafetyLabel(entry.Safety);
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

        private void DrawWorkspace()
        {
            DashboardModule[] modules = _catalog.VisibleWorkspaceModules
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

            if (modules.Length == 0)
            {
                DeactivateWorkspacePanel();
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_search)
                        ? DashboardText.NoWorkspacePanels
                        : DashboardText.NoWorkspaceSearchResults,
                    MessageType.Info);
                return;
            }

            EnsureWorkspaceSelection(modules);
            if (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact)
            {
                DrawCompactWorkspaceSelector(modules);
                DrawWorkspaceContent(modules);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawWorkspaceNavigation(modules);
                EditorGUILayout.Space(EditorUiTokens.SpaceSm);
                DrawWorkspaceContent(modules);
            }
        }

        private void DrawWorkspaceNavigation(IReadOnlyList<DashboardModule> modules)
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.Width(EditorUiTokens.DashboardSidebarWidth)))
            {
                GUILayout.Label(DashboardText.Workspace, EditorStyles.boldLabel);
                _workspaceNavigationScroll = EditorGUILayout.BeginScrollView(_workspaceNavigationScroll);
                foreach (DashboardModule module in modules)
                {
                    GUILayout.Label(module.DisplayName, EditorStyles.miniBoldLabel);
                    foreach (DashboardPanel panel in module.Panels)
                    {
                        if (EditorUiGUILayout.SelectionButton(
                                new GUIContent(panel.DisplayName, panel.Description),
                                string.Equals(_selectedPanelFullId, panel.FullId, StringComparison.Ordinal),
                                GUILayout.ExpandWidth(true),
                                GUILayout.Height(30f)))
                        {
                            SelectWorkspacePanel(panel.FullId);
                        }
                    }
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCompactWorkspaceSelector(IReadOnlyList<DashboardModule> modules)
        {
            DashboardPanel[] panels = modules.SelectMany(module => module.Panels).ToArray();
            GUIContent[] labels = panels.Select(panel =>
            {
                DashboardModule module = modules.First(item => string.Equals(item.ModuleId, panel.ModuleId, StringComparison.Ordinal));
                return new GUIContent(module.DisplayName + " · " + panel.DisplayName, panel.Description);
            }).ToArray();
            int index = Math.Max(0, Array.FindIndex(panels, panel =>
                string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal)));
            int next = EditorGUILayout.Popup(
                new GUIContent(DashboardText.Workspace, DashboardText.WorkspaceTooltip),
                index,
                labels);
            if (panels.Length > 0 && next >= 0 && next < panels.Length)
                SelectWorkspacePanel(panels[next].FullId);
            EditorGUILayout.Space(EditorUiTokens.SpaceSm);
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
                    GUILayout.Label(descriptor.DisplayName, EditorUiStyles.SectionTitle);
                    GUILayout.Label(module.DisplayName, EditorStyles.miniLabel);
                }
                GUILayout.FlexibleSpace();
                if (descriptor.Safety != DashboardEntrySafety.Navigation)
                    EditorUiGUILayout.Chip(new GUIContent(SafetyLabel(descriptor.Safety), SafetyTooltip(descriptor.Safety)));
                if (GUILayout.Button(new GUIContent("?", DashboardText.HelpTooltip), GUILayout.Width(28f)))
                    ShowHelp(module, null, descriptor);
            }
            EditorUiGUILayout.AccentLine(AccentColor);
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
                EditorGUILayout.HelpBox("面板加载失败。可在“系统”页复制诊断详情。", MessageType.Error);
                if (GUILayout.Button(new GUIContent(DashboardText.Retry, DashboardText.RetryTooltip)))
                {
                    _failedWorkspacePanels.Remove(descriptor.FullId);
                    RemoveWorkspaceDiagnostics(descriptor.FullId);
                }
                EditorGUILayout.EndScrollView();
                return;
            }

            if (EnsureActiveWorkspacePanel(descriptor))
            {
                _activePanelContext.AvailableWidth = Mathf.Min(EditorUiTokens.FormContentMaxWidth, Mathf.Max(240f, position.width -
                    (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact
                        ? 40f
                        : EditorUiTokens.DashboardSidebarWidth + 72f)));
                try
                {
                    using (EditorUiGUILayout.ConstrainedContent())
                        _activePanel.OnGUI(_activePanelContext);
                }
                catch (Exception exception)
                {
                    RecordWorkspaceFailure("workspace-panel-draw-failed", exception);
                }
            }
            EditorGUILayout.EndScrollView();
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
                !EditorUtility.DisplayDialog(DashboardText.ConfirmAction, action.Confirmation, "继续", "取消"))
            {
                return false;
            }
            action.Execute();
            return true;
        }

        private void SelectWorkspacePanel(string fullId)
        {
            if (string.Equals(_selectedPanelFullId, fullId, StringComparison.Ordinal))
                return;
            _selectedPanelFullId = fullId ?? string.Empty;
            _workspaceContentScroll = Vector2.zero;
            DeactivateWorkspacePanel();
        }

        private void EnsureWorkspaceSelection(IReadOnlyList<DashboardModule> modules)
        {
            if (modules.SelectMany(module => module.Panels)
                .Any(panel => string.Equals(panel.FullId, _selectedPanelFullId, StringComparison.Ordinal)))
            {
                return;
            }
            SelectWorkspacePanel(modules.SelectMany(module => module.Panels).First().FullId);
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

        private void ShowHelp(DashboardModule module, DashboardSurface surface, DashboardPanel panel)
        {
            _helpModule = module;
            _helpSurface = surface;
            _helpPanel = panel;
            _showHelp = true;
            Repaint();
        }

        private void DrawHelpDrawer()
        {
            if (!_showHelp)
                return;

            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(DashboardText.Help, EditorUiStyles.SectionTitle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            new GUIContent(DashboardText.Close, DashboardText.CloseHelpTooltip),
                            GUILayout.Width(48f)))
                    {
                        _showHelp = false;
                    }
                }

                string description = _helpPanel?.Description ?? _helpSurface?.Description ?? _helpModule?.Description;
                string usage = _helpPanel?.Usage ?? _helpSurface?.Usage;
                if (string.IsNullOrWhiteSpace(description))
                    description = DashboardText.HeaderSubtitle;

                GUILayout.Label(DashboardText.Purpose, EditorStyles.miniBoldLabel);
                GUILayout.Label(description, EditorStyles.wordWrappedLabel);
                if (!string.IsNullOrWhiteSpace(usage))
                {
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                    GUILayout.Label(DashboardText.Usage, EditorStyles.miniBoldLabel);
                    GUILayout.Label(usage, EditorStyles.wordWrappedLabel);
                }

                if (_helpModule != null)
                {
                    DashboardEntry[] documentedEntries = _helpSurface?.Entries
                        .Where(entry => !string.IsNullOrEmpty(entry.DocumentationPath) ||
                                        !string.IsNullOrEmpty(entry.DocumentationUrl))
                        .ToArray() ?? Array.Empty<DashboardEntry>();
                    if (documentedEntries.Length > 0)
                    {
                        EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                        GUILayout.Label(DashboardText.Documentation, EditorStyles.miniBoldLabel);
                        foreach (DashboardEntry entry in documentedEntries)
                        {
                            DrawDocumentationButtons(
                                string.IsNullOrEmpty(entry.SurfaceActionLabel) ? entry.DisplayName : entry.SurfaceActionLabel,
                                entry.DocumentationPath,
                                entry.DocumentationUrl,
                                _helpModule,
                                entry.Id);
                        }
                    }
                    else if (!string.IsNullOrEmpty(_helpModule.DocumentationPath) ||
                             !string.IsNullOrEmpty(_helpModule.DocumentationUrl))
                    {
                        EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                        GUILayout.Label(DashboardText.Documentation, EditorStyles.miniBoldLabel);
                        DrawDocumentationButtons(
                            _helpModule.DisplayName,
                            _helpModule.DocumentationPath,
                            _helpModule.DocumentationUrl,
                            _helpModule,
                            "module");
                    }
                }

                if (_helpModule != null)
                {
                    EditorGUILayout.Space(EditorUiTokens.SpaceXs);
                    GUILayout.Label(DashboardText.TechnicalDetails, EditorStyles.miniBoldLabel);
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
                    else
                    {
                        DrawTechnicalValue(_helpModule.Source.SourcePath);
                    }
                }
            }
            EditorGUILayout.Space(EditorUiTokens.SpaceXs);
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

            DashboardInstalledPackage[] packages = _catalog.InstalledPackages
                .Where(ShouldShowInstalledPackage)
                .Where(InstalledPackageMatchesSearch)
                .ToArray();
            _showInstalledPackages = EditorUiGUILayout.Disclosure(
                _showInstalledPackages,
                new GUIContent(
                    DashboardText.InstalledPackages(packages.Length),
                    "查看当前项目安装的 ZeroEngine 相关包及其连接状态。"));
            if (_showInstalledPackages)
            {
                using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
                {
                    for (int index = 0; index < packages.Length; index++)
                    {
                        DrawInstalledPackage(packages[index]);
                        if (index < packages.Length - 1)
                            EditorUiGUILayout.AccentLine(EditorUiPalette.Current.Border, 1f);
                    }
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
                    "查看由当前项目贡献并挂载到上游模块的适配器。"));
            if (_showProjectAdapters)
            {
                using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card))
                {
                    foreach (DashboardModule module in projectModules)
                    {
                        EditorUiGUILayout.ActionRow(
                            new GUIContent(module.DisplayName, module.Description),
                            new GUIContent(
                                DashboardText.ContributedTools(module.OwnedVisibleEntries.Count),
                                module.Description));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawInstalledPackage(DashboardInstalledPackage package)
        {
            DashboardModule module = _catalog.Modules.FirstOrDefault(item =>
                item.Source.Kind == DashboardSourceKind.Package &&
                string.Equals(item.Source.PackageName, package.Name, StringComparison.Ordinal));
            bool hasDescriptorError = _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath));
            string status = hasDescriptorError
                ? DashboardText.DescriptorIssue
                : module != null && module.VisibleEntries.Count > 0
                    ? DashboardText.ConnectedTools(module.VisibleEntries.Count)
                    : module != null
                        ? DashboardText.ConnectedNoTools
                        : DashboardText.NoToolsDeclared;
            EditorUiGUILayout.ActionRow(package.Name, status, () => EditorUiGUILayout.Chip(package.Version));
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

        private bool InstalledPackageMatchesSearch(DashboardInstalledPackage package)
        {
            if (string.IsNullOrEmpty(_search))
                return true;
            DashboardModule module = _catalog.Modules.FirstOrDefault(item =>
                item.Source.Kind == DashboardSourceKind.Package &&
                string.Equals(item.Source.PackageName, package.Name, StringComparison.Ordinal));
            return Matches(package.Name) ||
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

        private static string DiagnosticSeverityLabel(DashboardDiagnosticSeverity severity)
        {
            return severity == DashboardDiagnosticSeverity.Error ? "错误" : "警告";
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
}
