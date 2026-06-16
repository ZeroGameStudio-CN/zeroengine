using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringWindow : EditorWindow
    {
        private const float GroupWidth = 190f;
        private const float AssetWidth = 280f;
        private const float DrawerCollapsedHeight = 38f;
        private const float DrawerMinExpandedHeight = 220f;
        private const float DrawerMaxExpandedHeight = 360f;

        private readonly DataAuthoringInspectorHost _inspectorHost = new();
        private DataAuthoringProfile _profile;
        private IDataAuthoringAssetAdapter _selectedAdapter;
        private DataAuthoringAssetRecord _selectedRecord;
        private string _assetSearch = string.Empty;
        private string _issueSearch = string.Empty;
        private string _changeSearch = string.Empty;
        private string _importIssueSearch = string.Empty;
        private Vector2 _groupScroll;
        private Vector2 _assetScroll;
        private Vector2 _inspectorScroll;
        private Vector2 _issuesScroll;
        private Vector2 _changesScroll;
        private IReadOnlyList<DataAuthoringIssue> _currentIssues = Array.Empty<DataAuthoringIssue>();
        private DataAuthoringIssueScope _issueScope = DataAuthoringIssueScope.Selected;
        private bool _drawerExpanded;
        private DataAuthoringDrawerTab _drawerTab = DataAuthoringDrawerTab.Problems;
        private TabularImportPreview _importPreview;
        private string _importFolder = string.Empty;

        public static DataAuthoringWindow Open(string profileId)
        {
            var profile = DataAuthoringRegistry.GetProfile(profileId);
            if (profile == null)
            {
                throw new InvalidOperationException($"Data authoring profile '{profileId}' is not registered.");
            }

            return Open(profile);
        }

        public static DataAuthoringWindow Open(DataAuthoringProfile profile)
        {
            var window = GetWindow<DataAuthoringWindow>();
            window.Initialize(profile);
            window.Show();
            return window;
        }

        private void Initialize(DataAuthoringProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            titleContent = new GUIContent(_profile.Title);
            minSize = new Vector2(1080f, 620f);
            _selectedAdapter = _profile.Adapters.FirstOrDefault();
            _drawerExpanded = EditorPrefs.GetBool(GetDrawerExpandedKey(), false);
            SelectFirstAsset();
        }

        private void OnDisable()
        {
            _inspectorHost.Dispose();
        }

        private void OnGUI()
        {
            if (_profile == null)
            {
                EditorGUILayout.HelpBox("No data authoring profile selected.", MessageType.Info);
                return;
            }

            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawGroups();
                DrawAssets();
                DrawMainWorkspace();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_profile.Title, EditorStyles.boldLabel, GUILayout.Width(260f));
                GUILayout.FlexibleSpace();
                DrawToolsMenu();
            }
        }

        private void DrawToolsMenu()
        {
            var labels = Labels;
            if (!GUILayout.Button(labels.Tools, EditorStyles.toolbarDropDown, GUILayout.Width(80f)))
            {
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(labels.ValidateSelected), false, ValidateSelected);
            menu.AddItem(new GUIContent(labels.ValidateGroup), false, ValidateCurrentGroup);
            menu.AddItem(new GUIContent(labels.ValidateAll), false, ValidateAll);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(labels.ExportCsv), false, ExportCsv);
            if (_profile.ImportAdapters.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(labels.ImportPreview));
            }
            else
            {
                menu.AddItem(new GUIContent(labels.ImportPreview), false, RunImportPreview);
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(labels.Refresh), false, () => RefreshSelection());
            menu.DropDown(GUILayoutUtility.GetLastRect());
        }

        private void DrawGroups()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(GroupWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(Labels.Groups, EditorStyles.boldLabel);
                _groupScroll = EditorGUILayout.BeginScrollView(_groupScroll);
                foreach (var adapter in _profile.Adapters)
                {
                    var selected = adapter == _selectedAdapter;
                    if (GUILayout.Toggle(selected, adapter.DisplayName, "Button") != selected)
                    {
                        _selectedAdapter = adapter;
                        SelectFirstAsset();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAssets()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(AssetWidth), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(_selectedAdapter?.DisplayName ?? Labels.Assets, EditorStyles.boldLabel);
                _assetSearch = EditorGUILayout.TextField(_assetSearch, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField);
                _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll);
                foreach (var record in GetVisibleRecords())
                {
                    var selected = _selectedRecord?.Asset == record.Asset;
                    if (GUILayout.Toggle(selected, $"{record.StableId}  {record.DisplayName}", "Button") != selected)
                    {
                        SelectRecord(record);
                    }
                }

                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Labels.Create))
                    {
                        var asset = _selectedAdapter?.CreateAsset();
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            RefreshSelection(asset);
                        }
                    }

                    EditorGUI.BeginDisabledGroup(_selectedRecord?.Asset == null);
                    if (GUILayout.Button(Labels.Duplicate))
                    {
                        var asset = _selectedAdapter?.DuplicateAsset(_selectedRecord.Asset);
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            RefreshSelection(asset);
                        }
                    }

                    EditorGUI.EndDisabledGroup();
                }
            }
        }

        private void DrawMainWorkspace()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                DrawInspectorColumn();
                DrawBottomDrawer();
            }
        }

        private void DrawInspectorColumn()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                if (_selectedRecord?.Asset == null || _selectedAdapter == null)
                {
                    EditorGUILayout.HelpBox(Labels.SelectAsset, MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(_selectedRecord.DisplayName, EditorStyles.boldLabel);
                    if (GUILayout.Button(Labels.Ping, GUILayout.Width(70f)))
                    {
                        EditorGUIUtility.PingObject(_selectedRecord.Asset);
                    }
                }

                _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll, GUILayout.ExpandHeight(true));
                EditorGUI.BeginChangeCheck();
                if (_inspectorHost.UsesModernPipeline(_profile, _selectedRecord.Asset))
                {
                    _inspectorHost.Draw(_profile, _selectedAdapter, _selectedRecord, _currentIssues);
                }
                else
                {
                    _selectedAdapter.DrawInspector(_selectedRecord.Asset);
                }

                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(_selectedRecord.Asset);
                    ValidateSelected();
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawBottomDrawer()
        {
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox,
                       GUILayout.Height(GetDrawerHeight()),
                       GUILayout.ExpandWidth(true)))
            {
                DrawProblemStatusBar();
                if (!_drawerExpanded)
                {
                    return;
                }

                DrawDrawerTabs();
                if (_drawerTab == DataAuthoringDrawerTab.ImportPreview && _importPreview != null)
                {
                    DrawImportPreviewControls();
                    return;
                }

                DrawProblemTable();
            }
        }

        private void DrawDrawerTabs()
        {
            if (_importPreview == null)
            {
                _drawerTab = DataAuthoringDrawerTab.Problems;
                return;
            }

            var labels = Labels;
            var selected = GUILayout.Toolbar(
                (int)_drawerTab,
                new[] { labels.Problems, labels.ImportPreview },
                GUILayout.Height(22f),
                GUILayout.Width(220f));
            _drawerTab = (DataAuthoringDrawerTab)selected;
        }

        private void DrawProblemStatusBar()
        {
            var labels = Labels;
            var errors = _currentIssues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Error);
            var warnings = _currentIssues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Warning);
            var info = _currentIssues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Info);
            var summary = string.Format(
                labels.IssueSummaryFormat,
                labels.Problems,
                FormatIssueScope(_issueScope),
                errors,
                warnings,
                info);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(summary, EditorStyles.miniBoldLabel, GUILayout.MinWidth(180f));

                if (_importPreview != null)
                {
                    var importErrors = _importPreview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Error);
                    var importWarnings = _importPreview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Warning);
                    EditorGUILayout.LabelField(
                        string.Format(labels.ChangesSummaryFormat, _importPreview.Changes.Count, importErrors, importWarnings),
                        EditorStyles.miniLabel,
                        GUILayout.MinWidth(140f));
                }

                GUILayout.FlexibleSpace();

                if (_profile.Actions.OpenIssueDashboard != null
                    && !string.IsNullOrWhiteSpace(_profile.Actions.OpenIssueDashboardLabel))
                {
                    if (GUILayout.Button(_profile.Actions.OpenIssueDashboardLabel, EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    {
                        _profile.Actions.OpenIssueDashboard();
                    }
                }

                if (GUILayout.Button(_drawerExpanded ? labels.Collapse : labels.Expand, EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _drawerExpanded = !_drawerExpanded;
                    EditorPrefs.SetBool(GetDrawerExpandedKey(), _drawerExpanded);
                }
            }
        }

        private void DrawProblemTable()
        {
            var labels = Labels;
            using (new EditorGUILayout.HorizontalScope())
            {
                _issueSearch = DrawSearchField(_issueSearch, labels.SearchProblems);
            }

            var height = Mathf.Max(120f, GetDrawerHeight() - 86f);
            _issuesScroll = EditorGUILayout.BeginScrollView(_issuesScroll, GUILayout.Height(height));
            var table = DataAuthoringIssueTable.Build(_currentIssues, 200, _issueSearch, labels.IssueTableLabels);
            DataAuthoringIssueTable.Draw(table, PingIssue, labels.IssueTableLabels);
            if (table.HasOverflow)
            {
                EditorGUILayout.HelpBox(
                    string.Format(labels.IssueTableLabels.OverflowFormat, table.Rows.Count, table.TotalCount),
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private float GetDrawerHeight()
        {
            if (!_drawerExpanded)
            {
                return DrawerCollapsedHeight;
            }

            return Mathf.Clamp(position.height * 0.32f, DrawerMinExpandedHeight, DrawerMaxExpandedHeight);
        }

        private void DrawImportPreviewControls()
        {
            if (_importPreview == null)
            {
                return;
            }

            var errors = _importPreview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Error);
            var warnings = _importPreview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Warning);
            var labels = Labels;
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(labels.ImportPreview, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_importFolder, EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                string.Format(labels.ChangesSummaryFormat, _importPreview.Changes.Count, errors, warnings),
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_importPreview.HasBlockingErrors || _importPreview.Changes.Count == 0);
                if (GUILayout.Button(labels.ApplyImport, GUILayout.Width(110f)))
                {
                    ApplyImportPreview();
                    if (_importPreview == null)
                    {
                        return;
                    }
                }

                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button(labels.Clear, GUILayout.Width(70f)))
                {
                    _importPreview = null;
                    _importFolder = string.Empty;
                    _changeSearch = string.Empty;
                    _importIssueSearch = string.Empty;
                    _drawerTab = DataAuthoringDrawerTab.Problems;
                    return;
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(labels.ImportDiff, EditorStyles.boldLabel);
            _changeSearch = DrawSearchField(_changeSearch, labels.SearchDiffRows);
            _changesScroll = EditorGUILayout.BeginScrollView(_changesScroll, GUILayout.Height(140f));
            var changeTable = DataAuthoringChangeTable.Build(_importPreview.Changes, 200, _changeSearch);
            DataAuthoringChangeTable.Draw(changeTable, PingChange, labels.ChangeTableLabels);
            if (changeTable.HasOverflow)
            {
                EditorGUILayout.HelpBox(
                    string.Format(labels.ChangeTableLabels.OverflowFormat, changeTable.Rows.Count, changeTable.TotalCount),
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();

            if (_importPreview.Issues.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(labels.ImportIssues, EditorStyles.boldLabel);
                _importIssueSearch = DrawSearchField(_importIssueSearch, labels.SearchImportIssues);
                var issueTable = DataAuthoringIssueTable.Build(
                    ConvertImportIssues(_importPreview),
                    200,
                    _importIssueSearch,
                    labels.IssueTableLabels);
                DataAuthoringIssueTable.Draw(issueTable, PingIssue, labels.IssueTableLabels);
            }
        }

        private static string DrawSearchField(string value, string placeholder)
        {
            var style = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField;
            EditorGUILayout.LabelField(placeholder, EditorStyles.miniLabel, GUILayout.Width(96f));
            return EditorGUILayout.TextField(value ?? string.Empty, style);
        }

        private static void PingIssue(DataAuthoringIssue issue)
        {
            PingAssetPath(issue.AssetPath);
        }

        private static void PingChange(TabularImportChange change)
        {
            PingAssetPath(change.AssetPath);
        }

        private static void PingAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private IEnumerable<DataAuthoringAssetRecord> GetVisibleRecords()
        {
            if (_selectedAdapter == null)
            {
                yield break;
            }

            foreach (var record in _selectedAdapter.GetAssets())
            {
                if (record.MatchesSearch(_assetSearch))
                {
                    yield return record;
                }
            }
        }

        private void SelectFirstAsset()
        {
            SelectRecord(_selectedAdapter?.GetAssets().FirstOrDefault());
        }

        private void SelectRecord(DataAuthoringAssetRecord record)
        {
            _selectedRecord = record;
            Selection.activeObject = record?.Asset;
            _inspectorScroll = Vector2.zero;
            ValidateSelected();
        }

        private void ValidateSelected()
        {
            SetIssues(
                DataAuthoringIssueScope.Selected,
                _selectedAdapter != null && _selectedRecord?.Asset != null
                    ? _selectedAdapter.Validate(_selectedRecord.Asset)
                    : Array.Empty<DataAuthoringIssue>());
        }

        private void ValidateCurrentGroup()
        {
            SetIssues(
                DataAuthoringIssueScope.Group,
                _selectedAdapter != null
                    ? DataAuthoringValidationService.ValidateAdapter(_selectedAdapter)
                    : Array.Empty<DataAuthoringIssue>());
        }

        private void ValidateAll()
        {
            SetIssues(
                DataAuthoringIssueScope.All,
                _profile != null
                    ? DataAuthoringValidationService.ValidateProfile(_profile)
                    : Array.Empty<DataAuthoringIssue>());
        }

        private void RefreshSelection(Object target = null)
        {
            var targetObject = target ?? _selectedRecord?.Asset;
            var records = _selectedAdapter?.GetAssets() ?? Array.Empty<DataAuthoringAssetRecord>();
            SelectRecord(records.FirstOrDefault(record => record.Asset == targetObject) ?? records.FirstOrDefault());
        }

        private void ExportCsv()
        {
            var labels = Labels;
            var folder = EditorUtility.SaveFolderPanel(labels.ExportCsv, "Assets", labels.ExportCsvDefaultFolder);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var workbook = new TabularWorkbook();
            foreach (var adapter in _profile.Adapters)
            {
                adapter.AddExportSheets(workbook);
            }

            var exportIssues = DataAuthoringValidationService.ValidateProfile(_profile);
            DataAuthoringValidationService.AddValidationReportSheet(workbook, exportIssues);
            TabularCsvExporter.WriteCsvFolder(workbook, folder);
            Debug.Log($"[DataAuthoring] Exported CSV files to {folder.Replace('\\', '/')}");
        }

        private void RunImportPreview()
        {
            var labels = Labels;
            var folder = EditorUtility.OpenFolderPanel(labels.ImportFolderDialogTitle, "Assets", string.Empty);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                var workbook = TabularFolderImporter.ReadFolder(folder);
                _importPreview = DataAuthoringImportService.BuildPreview(workbook, _profile.ImportAdapters, createMissingAssets: false);
                _importFolder = folder.Replace('\\', '/');
                _changeSearch = string.Empty;
                _importIssueSearch = string.Empty;
                _drawerExpanded = true;
                _drawerTab = DataAuthoringDrawerTab.ImportPreview;
                EditorPrefs.SetBool(GetDrawerExpandedKey(), true);
                SetIssues(DataAuthoringIssueScope.ImportPreview, ConvertImportIssues(_importPreview));
                Repaint();
            }
            catch (Exception ex)
            {
                _importPreview = null;
                _importFolder = folder.Replace('\\', '/');
                _drawerExpanded = true;
                _drawerTab = DataAuthoringDrawerTab.Problems;
                EditorPrefs.SetBool(GetDrawerExpandedKey(), true);
                SetIssues(
                    DataAuthoringIssueScope.ImportPreview,
                    new[]
                    {
                        DataAuthoringIssue.Error(_importFolder, "Import", string.Empty, "folder", ex.Message)
                    });
            }
        }

        private void ApplyImportPreview()
        {
            if (_importPreview == null || _importPreview.HasBlockingErrors)
            {
                return;
            }

            var labels = Labels;
            if (!EditorUtility.DisplayDialog(
                    labels.ApplyImportDialogTitle,
                    string.Format(labels.ApplyImportDialogFormat, _importPreview.Changes.Count, _importFolder),
                    labels.Apply,
                    labels.Cancel))
            {
                return;
            }

            try
            {
                DataAuthoringImportService.Apply(_importPreview, _profile.ImportAdapters);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                _importPreview = null;
                _importFolder = string.Empty;
                _drawerTab = DataAuthoringDrawerTab.Problems;
                RefreshSelection();
                ValidateAll();
            }
            catch (Exception ex)
            {
                _drawerExpanded = true;
                _drawerTab = DataAuthoringDrawerTab.Problems;
                EditorPrefs.SetBool(GetDrawerExpandedKey(), true);
                SetIssues(
                    DataAuthoringIssueScope.ApplyImport,
                    new[]
                    {
                        DataAuthoringIssue.Error(_importFolder, "Import", string.Empty, "apply", ex.Message)
                    });
            }
        }

        private void SetIssues(DataAuthoringIssueScope scope, IReadOnlyList<DataAuthoringIssue> issues)
        {
            _issueScope = scope;
            _currentIssues = issues ?? Array.Empty<DataAuthoringIssue>();
        }

        private string FormatIssueScope(DataAuthoringIssueScope scope)
        {
            var labels = Labels;
            return scope switch
            {
                DataAuthoringIssueScope.Group => labels.IssueScopeGroup,
                DataAuthoringIssueScope.All => labels.IssueScopeAll,
                DataAuthoringIssueScope.ImportPreview => labels.IssueScopeImportPreview,
                DataAuthoringIssueScope.ApplyImport => labels.IssueScopeApplyImport,
                _ => labels.IssueScopeSelected
            };
        }

        private string GetDrawerExpandedKey()
        {
            return $"ZGS.DataAuthoring.{_profile.ProfileId}.BottomDrawerExpanded";
        }

        private static IReadOnlyList<DataAuthoringIssue> ConvertImportIssues(TabularImportPreview preview)
        {
            if (preview == null)
            {
                return Array.Empty<DataAuthoringIssue>();
            }

            return preview.Issues
                .Select(issue => new DataAuthoringIssue(
                    issue.Severity,
                    string.IsNullOrWhiteSpace(issue.AssetPath) ? issue.SheetName : issue.AssetPath,
                    "Import",
                    issue.StableId,
                    string.IsNullOrWhiteSpace(issue.FieldPath) ? issue.ColumnName : issue.FieldPath,
                    issue.Message))
                .ToArray();
        }

        private DataAuthoringWindowLabels Labels => _profile?.Labels ?? DataAuthoringWindowLabels.Default;

        private enum DataAuthoringIssueScope
        {
            Selected,
            Group,
            All,
            ImportPreview,
            ApplyImport
        }

        private enum DataAuthoringDrawerTab
        {
            Problems,
            ImportPreview
        }
    }
}
