using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using ZeroEngine.Editor.Dashboard;

namespace ZeroEngine.Editor
{
    public sealed class ZeroEngineDashboard : EditorWindow
    {
        private static readonly string[] PageNames = { "Tools", "Installed", "Diagnostics" };

        private readonly Dictionary<string, DashboardDiagnostic> _runtimeDiagnostics =
            new Dictionary<string, DashboardDiagnostic>(StringComparer.Ordinal);

        private DashboardCatalog _catalog = DashboardCatalog.Empty;
        private int _page;
        private string _search = string.Empty;
        private string _selectedModuleId = string.Empty;
        private Vector2 _moduleScroll;
        private Vector2 _contentScroll;
        private Vector2 _installedScroll;
        private Vector2 _diagnosticScroll;
        private GUIStyle _heroTitleStyle;
        private GUIStyle _heroSubtitleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _metricStyle;

        [MenuItem("ZeroEngine/Dashboard")]
        public static void ShowWindow()
        {
            ZeroEngineDashboard window = GetWindow<ZeroEngineDashboard>("ZeroEngine Dashboard");
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
            EnsureStyles();
            DrawHeader();
            DrawNavigation();
            switch (_page)
            {
                case 0:
                    DrawTools();
                    break;
                case 1:
                    DrawInstalled();
                    break;
                default:
                    DrawDiagnostics();
                    break;
            }
        }

        private void EnsureStyles()
        {
            if (_heroTitleStyle != null)
                return;

            _heroTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                fixedHeight = 25f
            };
            _heroSubtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11
            };
            _heroSubtitleStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.72f, 0.76f, 0.82f)
                : new Color(0.30f, 0.34f, 0.40f);
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                fixedHeight = 20f
            };
            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 4, 6)
            };
            _metricStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 4, 4),
                alignment = TextAnchor.MiddleLeft
            };
        }

        private void DrawHeader()
        {
            DrawAccentLine(AccentColor, 3f);
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label("ZeroEngine Dashboard", _heroTitleStyle);
                        GUILayout.Label(
                            "Installed modules, project adapters, and diagnostics in one safe Editor workspace.",
                            _heroSubtitleStyle);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(
                            EditorGUIUtility.IconContent("Refresh", "Refresh module catalog"),
                            GUILayout.Width(34f),
                            GUILayout.Height(30f)))
                    {
                        RefreshCatalog();
                    }
                }

                EditorGUILayout.Space(6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    int diagnosticCount = _catalog.Diagnostics.Count + _runtimeDiagnostics.Count;
                    int toolCount = _catalog.VisibleModules.Sum(module => module.VisibleEntries.Count);
                    DrawMetric("MODULES", _catalog.VisibleModules.Count, AccentColor);
                    DrawMetric("TOOLS", toolCount, SuccessColor);
                    DrawMetric("DIAGNOSTICS", diagnosticCount, diagnosticCount == 0 ? SuccessColor : WarningColor);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawNavigation()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                _page = GUILayout.Toolbar(_page, PageNames, GUILayout.Width(360f), GUILayout.Height(28f));
                GUILayout.FlexibleSpace();

                GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                       GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                       EditorStyles.textField;
                _search = GUILayout.TextField(_search ?? string.Empty, searchStyle, GUILayout.MinWidth(180f), GUILayout.Height(22f));
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_search)))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(48f), GUILayout.Height(22f)))
                        _search = string.Empty;
                }
            }
            EditorGUILayout.Space(6f);
        }

        private void DrawTools()
        {
            IReadOnlyList<DashboardModule> modules = _catalog.VisibleModules;
            if (modules.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No module tools are currently declared. Install a package with a valid descriptor or add a project adapter.",
                    MessageType.Info);
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
            using (new EditorGUILayout.VerticalScope(_cardStyle, GUILayout.Width(224f)))
            {
                GUILayout.Label("MODULES", EditorStyles.miniBoldLabel);
                GUILayout.Label("Installed hosts", EditorStyles.miniLabel);
                DrawAccentLine(AccentColor, 2f);
                EditorGUILayout.Space(4f);
                _moduleScroll = EditorGUILayout.BeginScrollView(_moduleScroll);
                int allToolCount = modules.Sum(module => module.VisibleEntries.Count);
                if (DrawSelectionButton("All tools", allToolCount, string.IsNullOrEmpty(_selectedModuleId)))
                    _selectedModuleId = string.Empty;

                foreach (DashboardModule module in modules)
                {
                    if (!ModuleMatchesSearch(module))
                        continue;
                    if (DrawSelectionButton(
                            module.DisplayName,
                            module.VisibleEntries.Count,
                            string.Equals(_selectedModuleId, module.ModuleId, StringComparison.Ordinal)))
                    {
                        _selectedModuleId = module.ModuleId;
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private static bool DrawSelectionButton(string label, int toolCount, bool selected)
        {
            Color previous = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = AccentColor;
            bool clicked = GUILayout.Button(
                label + "  ·  " + toolCount,
                selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(30f));
            GUI.backgroundColor = previous;
            return clicked;
        }

        private void DrawToolContent(IReadOnlyList<DashboardModule> modules)
        {
            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
            IEnumerable<DashboardModule> selectedModules = string.IsNullOrEmpty(_selectedModuleId)
                ? modules
                : modules.Where(module => string.Equals(module.ModuleId, _selectedModuleId, StringComparison.Ordinal));

            int drawnEntries = 0;
            foreach (DashboardModule module in selectedModules)
            {
                DashboardEntry[] entries = module.VisibleEntries.Where(EntryMatchesSearch).ToArray();
                if (entries.Length == 0 && !ModuleTextMatchesSearch(module))
                    continue;

                DrawModuleHeader(module);
                foreach (DashboardEntry entry in entries)
                {
                    DrawEntry(entry);
                    drawnEntries++;
                }
                EditorGUILayout.Space(8);
            }

            if (drawnEntries == 0)
                EditorGUILayout.HelpBox("No tools match the current search.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawModuleHeader(DashboardModule module)
        {
            DrawAccentLine(AccentColor, 2f);
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(module.DisplayName, _sectionTitleStyle);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(module.VisibleEntries.Count + " tools", EditorStyles.miniBoldLabel);
                    if (!string.IsNullOrEmpty(module.DocumentationPath) && GUILayout.Button("Docs", GUILayout.Width(48f)))
                        OpenLocalDocumentation(module);
                    if (!string.IsNullOrEmpty(module.DocumentationUrl) && GUILayout.Button("Web", GUILayout.Width(48f)))
                        Application.OpenURL(module.DocumentationUrl);
                }
                if (!string.IsNullOrEmpty(module.Description))
                    EditorGUILayout.LabelField(module.Description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(module.ModuleId, EditorStyles.miniLabel);
            }
        }

        private void DrawEntry(DashboardEntry entry)
        {
            bool available = IsAvailable(entry);
            bool failed = _runtimeDiagnostics.ContainsKey(entry.FullId);
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);
                        string ownerLabel = GetMountedOwnerLabel(entry);
                        if (!string.IsNullOrEmpty(ownerLabel))
                            DrawStatusLabel("ADAPTER · " + ownerLabel, AccentColor);
                        GUILayout.Label(entry.Category.ToUpperInvariant() + " · " + SafetyLabel(entry.Safety).ToUpperInvariant(), EditorStyles.miniLabel);
                        if (!string.IsNullOrEmpty(entry.Description))
                            EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedMiniLabel);
                        EditorGUILayout.SelectableLabel(entry.MenuPath, EditorStyles.miniLabel, GUILayout.Height(16f));
                    }

                    GUILayout.Space(12f);
                    using (new EditorGUI.DisabledScope(!available || failed))
                    {
                        string actionLabel = entry.Kind == DashboardEntryKind.Window ? "Open" : "Run";
                        Color previous = GUI.backgroundColor;
                        GUI.backgroundColor = AccentColor;
                        if (GUILayout.Button(actionLabel, GUILayout.Width(92f), GUILayout.Height(36f)))
                            ExecuteEntry(entry);
                        GUI.backgroundColor = previous;
                    }
                }

                if (!available)
                {
                    EditorGUILayout.HelpBox(
                        entry.Availability == DashboardEntryAvailability.EditMode
                            ? "Available only in Edit Mode."
                            : "Available only in Play Mode.",
                        MessageType.Info);
                }
                if (_runtimeDiagnostics.TryGetValue(entry.FullId, out DashboardDiagnostic diagnostic))
                    EditorGUILayout.HelpBox(diagnostic.Message, MessageType.Error);
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
                "Documentation path does not exist: " + module.DocumentationPath,
                module.Source.SourcePath,
                module.ModuleId);
        }

        private void DrawInstalled()
        {
            _installedScroll = EditorGUILayout.BeginScrollView(_installedScroll);
            DrawPageTitle("Installed modules", "Registered packages and project adapters connected to Dashboard.");
            foreach (DashboardInstalledPackage package in _catalog.InstalledPackages.Where(ShouldShowInstalledPackage))
            {
                DashboardModule module = _catalog.Modules.FirstOrDefault(item =>
                    item.Source.Kind == DashboardSourceKind.Package &&
                    string.Equals(item.Source.PackageName, package.Name, StringComparison.Ordinal));
                bool hasDescriptorError = _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath));

                using (new EditorGUILayout.VerticalScope(_cardStyle))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(package.Name, EditorStyles.boldLabel);
                        GUILayout.Label(package.Version, EditorStyles.miniLabel, GUILayout.Width(80));
                    }

                    if (hasDescriptorError)
                    {
                        DrawStatusLabel("Descriptor issue · see Diagnostics", ErrorColor);
                    }
                    else if (module != null && module.VisibleEntries.Count > 0)
                    {
                        DrawStatusLabel("Connected · visible tools: " + module.VisibleEntries.Count, SuccessColor);
                    }
                    else if (module != null)
                    {
                        DrawStatusLabel("Connected · no direct tools (empty, mounted, or replaced)", MutedColor);
                    }
                    else
                    {
                        DrawStatusLabel("No tools declared", MutedColor);
                    }
                }
            }

            DashboardModule[] projectModules = _catalog.Modules
                .Where(module => module.Source.Kind == DashboardSourceKind.Project)
                .ToArray();
            if (projectModules.Length > 0)
            {
                EditorGUILayout.Space(8);
                DrawPageTitle("Project adapters", "Project-owned profiles mounted into installed modules.");
                foreach (DashboardModule module in projectModules)
                {
                    using (new EditorGUILayout.VerticalScope(_cardStyle))
                    {
                        EditorGUILayout.LabelField(module.DisplayName, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(
                            module.ModuleId + " · contributed tools: " + module.OwnedVisibleEntries.Count,
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
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

        private void DrawDiagnostics()
        {
            _diagnosticScroll = EditorGUILayout.BeginScrollView(_diagnosticScroll);
            DashboardDiagnostic[] diagnostics = _catalog.Diagnostics
                .Concat(_runtimeDiagnostics.Values)
                .Where(DiagnosticMatchesSearch)
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.SourcePath, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray();

            DrawPageTitle("Diagnostics", "Descriptor, mount, replacement, and execution health.");
            if (diagnostics.Length == 0)
            {
                DrawAccentLine(SuccessColor, 2f);
                using (new EditorGUILayout.VerticalScope(_cardStyle))
                {
                    DrawStatusLabel("Healthy · no diagnostics", SuccessColor);
                    GUILayout.Label("All discovered Dashboard descriptors are valid.", EditorStyles.wordWrappedMiniLabel);
                }
            }
            else
            {
                foreach (DashboardDiagnostic diagnostic in diagnostics)
                {
                    MessageType type = diagnostic.Severity == DashboardDiagnosticSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning;
                    string title = "[" + diagnostic.Severity + "] " + diagnostic.Code;
                    EditorGUILayout.HelpBox(title + "\n" + diagnostic.Message, type);
                    if (!string.IsNullOrEmpty(diagnostic.SourcePath))
                        EditorGUILayout.SelectableLabel(diagnostic.SourcePath, EditorStyles.miniLabel, GUILayout.Height(16));
                    if (!string.IsNullOrEmpty(diagnostic.MenuPath))
                        EditorGUILayout.SelectableLabel(diagnostic.MenuPath, EditorStyles.miniLabel, GUILayout.Height(16));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ModuleMatchesSearch(DashboardModule module)
        {
            return ModuleTextMatchesSearch(module) || module.VisibleEntries.Any(EntryMatchesSearch);
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
                   Matches(entry.MenuPath) ||
                   Matches(entry.FullId) ||
                   Matches(entry.ModuleId);
        }

        private string GetMountedOwnerLabel(DashboardEntry entry)
        {
            if (string.Equals(entry.ModuleId, entry.DisplayModuleId, StringComparison.Ordinal))
                return string.Empty;
            DashboardModule owner = _catalog.Modules.FirstOrDefault(module =>
                string.Equals(module.ModuleId, entry.ModuleId, StringComparison.Ordinal));
            return owner == null ? entry.ModuleId : owner.DisplayName;
        }

        private void DrawMetric(string label, int value, Color color)
        {
            using (new EditorGUILayout.HorizontalScope(_metricStyle, GUILayout.Width(126f), GUILayout.Height(26f)))
            {
                DrawStatusLabel("●", color, GUILayout.Width(12f));
                GUILayout.Label(value + " " + label, EditorStyles.miniBoldLabel);
            }
        }

        private void DrawPageTitle(string title, string subtitle)
        {
            DrawAccentLine(AccentColor, 2f);
            GUILayout.Label(title, _sectionTitleStyle);
            GUILayout.Label(subtitle, _heroSubtitleStyle);
            EditorGUILayout.Space(5f);
        }

        private static void DrawAccentLine(Color color, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color);
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
                case DashboardEntrySafety.ReadOnly: return "read-only";
                case DashboardEntrySafety.ProjectWrite: return "project-write";
                case DashboardEntrySafety.Destructive: return "destructive";
                default: return "navigation";
            }
        }

        private static Color AccentColor => EditorGUIUtility.isProSkin
            ? new Color(0.36f, 0.67f, 1f)
            : new Color(0.12f, 0.42f, 0.74f);

        private static Color SuccessColor => EditorGUIUtility.isProSkin
            ? new Color(0.48f, 0.86f, 0.56f)
            : new Color(0.12f, 0.55f, 0.24f);

        private static Color WarningColor => EditorGUIUtility.isProSkin
            ? new Color(1f, 0.72f, 0.30f)
            : new Color(0.72f, 0.40f, 0.04f);

        private static Color ErrorColor => EditorGUIUtility.isProSkin
            ? new Color(1f, 0.48f, 0.45f)
            : new Color(0.72f, 0.16f, 0.12f);

        private static Color MutedColor => EditorGUIUtility.isProSkin
            ? new Color(0.68f, 0.71f, 0.76f)
            : new Color(0.36f, 0.39f, 0.44f);

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
