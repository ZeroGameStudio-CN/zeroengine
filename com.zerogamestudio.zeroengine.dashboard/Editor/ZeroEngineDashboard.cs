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

        [MenuItem("ZeroEngine/Dashboard")]
        public static void ShowWindow()
        {
            GetWindow<ZeroEngineDashboard>("ZeroEngine Dashboard");
        }

        private void OnEnable()
        {
            UnityEditor.PackageManager.Events.registeredPackages += OnRegisteredPackages;
            DashboardDescriptorAssetPostprocessor.DescriptorsChanged += RefreshCatalog;
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
            DrawToolbar();
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

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("ZeroEngine", EditorStyles.boldLabel, GUILayout.Width(82));
                GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSearchTextField") ??
                                       GUI.skin.FindStyle("ToolbarSeachTextField") ??
                                       EditorStyles.textField;
                _search = GUILayout.TextField(_search ?? string.Empty, searchStyle, GUILayout.MinWidth(120));
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(44)))
                    _search = string.Empty;
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                    RefreshCatalog();

                int diagnosticCount = _catalog.Diagnostics.Count + _runtimeDiagnostics.Count;
                GUILayout.Label("Diagnostics: " + diagnosticCount, EditorStyles.miniLabel, GUILayout.Width(92));
                _page = GUILayout.Toolbar(_page, PageNames, EditorStyles.toolbarButton, GUILayout.Width(260));
            }
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
                DrawToolContent(modules);
            }
        }

        private void DrawModuleList(IReadOnlyList<DashboardModule> modules)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(210)))
            {
                GUILayout.Label("Modules", EditorStyles.boldLabel);
                _moduleScroll = EditorGUILayout.BeginScrollView(_moduleScroll);
                if (DrawSelectionButton("All", string.IsNullOrEmpty(_selectedModuleId)))
                    _selectedModuleId = string.Empty;

                foreach (DashboardModule module in modules)
                {
                    if (!ModuleMatchesSearch(module))
                        continue;
                    if (DrawSelectionButton(
                            module.DisplayName,
                            string.Equals(_selectedModuleId, module.ModuleId, StringComparison.Ordinal)))
                    {
                        _selectedModuleId = module.ModuleId;
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private static bool DrawSelectionButton(string label, bool selected)
        {
            GUIStyle style = selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
            return GUILayout.Button(label, style, GUILayout.ExpandWidth(true), GUILayout.Height(24));
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
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(module.DisplayName, EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(module.DocumentationPath) && GUILayout.Button("Docs", GUILayout.Width(48)))
                    OpenLocalDocumentation(module);
                if (!string.IsNullOrEmpty(module.DocumentationUrl) && GUILayout.Button("Web", GUILayout.Width(48)))
                    Application.OpenURL(module.DocumentationUrl);
            }
            if (!string.IsNullOrEmpty(module.Description))
                EditorGUILayout.LabelField(module.Description, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(module.ModuleId, EditorStyles.miniLabel);
        }

        private void DrawEntry(DashboardEntry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(entry.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(entry.Category + " · " + SafetyLabel(entry.Safety), EditorStyles.miniLabel);
                }

                if (!string.IsNullOrEmpty(entry.Description))
                    EditorGUILayout.LabelField(entry.Description, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.SelectableLabel(entry.MenuPath, EditorStyles.miniLabel, GUILayout.Height(16));

                bool available = IsAvailable(entry);
                bool failed = _runtimeDiagnostics.ContainsKey(entry.FullId);
                using (new EditorGUI.DisabledScope(!available || failed))
                {
                    string actionLabel = entry.Kind == DashboardEntryKind.Window ? "Open" : "Run";
                    if (GUILayout.Button(actionLabel, GUILayout.Height(26)))
                        ExecuteEntry(entry);
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
            foreach (DashboardInstalledPackage package in _catalog.InstalledPackages.Where(ShouldShowInstalledPackage))
            {
                DashboardModule module = _catalog.Modules.FirstOrDefault(item =>
                    item.Source.Kind == DashboardSourceKind.Package &&
                    string.Equals(item.Source.PackageName, package.Name, StringComparison.Ordinal));
                bool hasDescriptorError = _catalog.Diagnostics.Any(item => IsBelow(item.SourcePath, package.ResolvedPath));

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(package.Name, EditorStyles.boldLabel);
                        GUILayout.Label(package.Version, EditorStyles.miniLabel, GUILayout.Width(80));
                    }

                    if (module != null)
                    {
                        string state = module.VisibleEntries.Count > 0
                            ? "Connected · visible tools: " + module.VisibleEntries.Count
                            : "Connected · no visible tools (empty, isolated, or replaced)";
                        EditorGUILayout.LabelField(state, EditorStyles.wordWrappedMiniLabel);
                    }
                    else if (hasDescriptorError)
                    {
                        EditorGUILayout.LabelField("Descriptor error · see Diagnostics", EditorStyles.wordWrappedMiniLabel);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("No tools declared", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }

            DashboardModule[] projectModules = _catalog.Modules
                .Where(module => module.Source.Kind == DashboardSourceKind.Project)
                .ToArray();
            if (projectModules.Length > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Project adapters", EditorStyles.boldLabel);
                foreach (DashboardModule module in projectModules)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(module.DisplayName, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(
                            module.ModuleId + " · visible tools: " + module.VisibleEntries.Count,
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

            if (diagnostics.Length == 0)
            {
                EditorGUILayout.HelpBox("No diagnostics.", MessageType.Info);
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
                   Matches(entry.FullId);
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
