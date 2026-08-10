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
            new GUIContent(DashboardText.System, DashboardText.SystemTooltip)
        };

        private readonly Dictionary<string, DashboardDiagnostic> _runtimeDiagnostics =
            new Dictionary<string, DashboardDiagnostic>(StringComparer.Ordinal);

        private DashboardCatalog _catalog = DashboardCatalog.Empty;
        private int _page;
        private string _search = string.Empty;
        private string _selectedModuleId = string.Empty;
        private Vector2 _moduleScroll;
        private Vector2 _contentScroll;
        private Vector2 _systemScroll;
        private bool _showInstalledPackages;
        private bool _showProjectAdapters;
        private readonly HashSet<string> _expandedDetails = new HashSet<string>(StringComparer.Ordinal);

        [MenuItem("ZeroEngine/Dashboard")]
        public static void ShowWindow()
        {
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>(DashboardText.WindowTitle);
            window.titleContent = new GUIContent(DashboardText.WindowTitle, DashboardText.ToolsTooltip);
            window.minSize = new Vector2(760f, 460f);
        }

        private void OnEnable()
        {
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged += RefreshCatalog;
            minSize = new Vector2(760f, 460f);
            RefreshCatalog();
        }

        private void OnDisable()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged -= RefreshCatalog;
        }

        private void OnRegisteredPackages(PackageRegistrationEventArgs eventArgs)
        {
            RefreshCatalog();
        }

        private void RefreshCatalog()
        {
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
            Repaint();
        }

        private void OnGUI()
        {
            EditorUiStyles.EnsureCurrent();
            DrawHeader();
            DrawNavigation();
            switch (_page)
            {
                case 0:
                    DrawTools();
                    break;
                default:
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
                    if (!compact)
                    {
                        DrawInlineMetric(DashboardText.ModuleCount(_catalog.VisibleModules.Count), AccentColor);
                        DrawInlineMetric(DashboardText.ToolCount(toolCount), SuccessColor);
                    }
                    DrawInlineMetric(DashboardText.IssueCount(diagnosticCount), diagnosticCount == 0 ? SuccessColor : WarningColor);
                    if (GUILayout.Button(
                            EditorGUIUtility.IconContent("Refresh", DashboardText.RefreshTooltip),
                            GUILayout.Width(30f),
                            GUILayout.Height(24f)))
                    {
                        RefreshCatalog();
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

            if (EditorUiGUILayout.ResponsiveMode(position.width) == EditorUiResponsiveMode.Compact)
            {
                DrawCompactModuleSelector(modules);
                DrawToolContent(modules);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawModuleList(modules);
                EditorGUILayout.Space(8f);
                DrawToolContent(modules);
            }
        }

        private void DrawModuleList(IReadOnlyList<DashboardModule> modules)
        {
            using (new EditorGUILayout.VerticalScope(EditorUiStyles.Card, GUILayout.Width(EditorUiTokens.DashboardSidebarWidth)))
            {
                GUILayout.Label(DashboardText.Modules, EditorStyles.boldLabel);
                _moduleScroll = EditorGUILayout.BeginScrollView(_moduleScroll);
                int allToolCount = modules.Sum(module => module.VisibleEntries.Count);
                if (DrawSelectionButton(
                        DashboardText.AllTools,
                        DashboardText.ToolsTooltip,
                        allToolCount,
                        string.IsNullOrEmpty(_selectedModuleId)))
                    _selectedModuleId = string.Empty;

                foreach (DashboardModule module in modules)
                {
                    if (!ModuleMatchesSearch(module))
                        continue;
                    if (DrawSelectionButton(
                            module.DisplayName,
                            module.Description,
                            module.VisibleEntries.Count,
                            string.Equals(_selectedModuleId, module.ModuleId, StringComparison.Ordinal)))
                    {
                        _selectedModuleId = module.ModuleId;
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCompactModuleSelector(IReadOnlyList<DashboardModule> modules)
        {
            var visible = modules.Where(ModuleMatchesSearch).ToList();
            var ids = new List<string> { string.Empty };
            var labels = new List<GUIContent>
            {
                new GUIContent(DashboardText.AllTools, DashboardText.ToolsTooltip)
            };
            foreach (DashboardModule module in visible)
            {
                ids.Add(module.ModuleId);
                labels.Add(new GUIContent(
                    module.DisplayName + "（" + module.VisibleEntries.Count + "）",
                    module.Description));
            }

            int index = Math.Max(0, ids.FindIndex(id => string.Equals(id, _selectedModuleId, StringComparison.Ordinal)));
            int selected = EditorGUILayout.Popup(
                new GUIContent(DashboardText.Modules, DashboardText.ModuleSelectorTooltip),
                index,
                labels.ToArray());
            _selectedModuleId = ids[Mathf.Clamp(selected, 0, ids.Count - 1)];
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
            IEnumerable<DashboardModule> selectedModules = string.IsNullOrEmpty(_selectedModuleId)
                ? modules
                : modules.Where(module => string.Equals(module.ModuleId, _selectedModuleId, StringComparison.Ordinal));

            int drawnSurfaces = 0;
            foreach (DashboardModule module in selectedModules)
            {
                DashboardSurface[] surfaces = module.VisibleSurfaces.Where(SurfaceMatchesSearch).ToArray();
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
            EditorGUILayout.EndScrollView();
        }

        private void DrawModuleHeader(DashboardModule module)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(module.DisplayName, EditorUiStyles.SectionTitle);
                    if (!string.IsNullOrEmpty(module.Description))
                        EditorGUILayout.LabelField(module.Description, EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(DashboardText.ToolCount(module.VisibleEntries.Count), EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(module.DocumentationPath) && GUILayout.Button(
                        new GUIContent(DashboardText.Documentation, DashboardText.DocumentationTooltip),
                        GUILayout.Width(48f)))
                    OpenLocalDocumentation(module);
                if (!string.IsNullOrEmpty(module.DocumentationUrl) && GUILayout.Button(
                        new GUIContent(DashboardText.Website, DashboardText.WebsiteTooltip),
                        GUILayout.Width(48f)))
                    Application.OpenURL(module.DocumentationUrl);
            }
            EditorUiGUILayout.AccentLine(AccentColor);
            EditorGUILayout.Space(EditorUiTokens.SpaceXs);
        }

        private void DrawSurface(DashboardModule module, DashboardSurface surface)
        {
            EditorUiGUILayout.ActionRow(
                new GUIContent(surface.DisplayName, surface.Description),
                new GUIContent(surface.Description, surface.Description),
                () => DrawSurfaceActions(surface));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorUiTokens.SpaceSm);
                string context = SurfaceContextLabel(surface);
                if (!string.IsNullOrEmpty(context))
                    EditorUiGUILayout.Chip(context);
                DashboardEntrySafety safety = surface.DefaultEntry.Safety;
                if (safety != DashboardEntrySafety.Navigation)
                    EditorUiGUILayout.Chip(new GUIContent(SafetyLabel(safety), SafetyTooltip(safety)));
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
                foreach (DashboardEntry entry in surface.Entries)
                {
                    EditorGUILayout.SelectableLabel(
                        entry.FullId + "  ·  " + entry.MenuPath,
                        EditorStyles.miniLabel,
                        GUILayout.Height(16f));
                }
            }

            foreach (DashboardEntry entry in surface.Entries)
            {
                if (!IsAvailable(entry))
                {
                    EditorGUILayout.HelpBox(
                        entry.Availability == DashboardEntryAvailability.EditMode
                            ? DashboardText.EditModeOnly(entry.DisplayName)
                            : DashboardText.PlayModeOnly(entry.DisplayName),
                        MessageType.Info);
                }
                if (_runtimeDiagnostics.TryGetValue(entry.FullId, out DashboardDiagnostic diagnostic))
                    EditorGUILayout.HelpBox(entry.DisplayName + ": " + diagnostic.Message, MessageType.Error);
            }
        }

        private void DrawSurfaceActions(DashboardSurface surface)
        {
            foreach (DashboardEntry entry in surface.Entries)
            {
                bool available = IsAvailable(entry);
                bool failed = _runtimeDiagnostics.ContainsKey(entry.FullId);
                string label = string.IsNullOrEmpty(entry.SurfaceActionLabel)
                    ? entry.Kind == DashboardEntryKind.Window ? DashboardText.Open : DashboardText.Run
                    : entry.SurfaceActionLabel;
                var content = new GUIContent(label, entry.Description);
                using (new EditorGUI.DisabledScope(!available || failed))
                {
                    bool clicked = entry == surface.DefaultEntry
                        ? EditorUiGUILayout.PrimaryButton(content, GUILayout.MinWidth(72f))
                        : GUILayout.Button(content, GUILayout.MinWidth(72f), GUILayout.Height(EditorUiTokens.PrimaryButtonHeight));
                    if (clicked)
                        ExecuteEntry(entry);
                }
            }
        }

        private void ExecuteEntry(DashboardEntry entry)
        {
            DashboardExecutionResult result = DashboardEntryExecutor.Execute(entry);
            if (result.Status != DashboardExecutionStatus.MenuMissing &&
                result.Status != DashboardExecutionStatus.Failed)
            {
                return;
            }

            _runtimeDiagnostics[entry.FullId] = new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Error,
                result.Status == DashboardExecutionStatus.MenuMissing ? "menu-execution-failed" : "menu-execution-exception",
                result.Message,
                entry.SourcePath,
                entry.ModuleId,
                entry.Id,
                entry.MenuPath);
            Repaint();
        }

        private void OpenLocalDocumentation(DashboardModule module)
        {
            if (File.Exists(module.DocumentationPath) || Directory.Exists(module.DocumentationPath))
            {
                EditorUtility.RevealInFinder(module.DocumentationPath);
                return;
            }

            string key = module.ModuleId + "/documentation";
            _runtimeDiagnostics[key] = new DashboardDiagnostic(
                DashboardDiagnosticSeverity.Warning,
                "documentation-missing",
                DashboardText.DocumentationMissing(module.DocumentationPath),
                module.Source.SourcePath,
                module.ModuleId);
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
            return Matches(module.DisplayName) || Matches(module.Description) || Matches(module.ModuleId);
        }

        private bool EntryMatchesSearch(DashboardEntry entry)
        {
            return Matches(entry.DisplayName) ||
                   Matches(entry.Description) ||
                   Matches(entry.Category) ||
                   Matches(entry.Section) ||
                   Matches(entry.SurfaceDisplayName) ||
                   Matches(entry.SurfaceActionLabel) ||
                   Matches(entry.MenuPath) ||
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
