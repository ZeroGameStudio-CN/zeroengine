using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringWindow : EditorWindow
    {
        private DataAuthoringProfile _profile;
        private IDataAuthoringAssetAdapter _selectedAdapter;
        private DataAuthoringAssetRecord _selectedRecord;
        private TabularImportPreview _importPreview;
        private Vector2 _assetScroll;
        private Vector2 _detailScroll;

        public static DataAuthoringWindow Open(string profileId)
        {
            var profile = DataAuthoringRegistry.GetProfile(profileId);
            var window = GetWindow<DataAuthoringWindow>();
            window.Initialize(profile);
            window.Show();
            return window;
        }

        private void Initialize(DataAuthoringProfile profile)
        {
            _profile = profile ?? new DataAuthoringProfile("EMPTY", "Data Authoring", Array.Empty<IDataAuthoringAssetAdapter>());
            titleContent = new GUIContent(_profile.Title);
            minSize = new Vector2(980f, 560f);
            _selectedAdapter = _profile.Adapters.FirstOrDefault();
            _selectedRecord = null;
            _importPreview = null;
        }

        private void OnEnable()
        {
            if (_profile == null)
            {
                var first = DataAuthoringRegistry.RegisteredProfiles.FirstOrDefault();
                Initialize(first);
            }
        }

        private void OnGUI()
        {
            if (_profile == null)
            {
                Initialize(DataAuthoringRegistry.RegisteredProfiles.FirstOrDefault());
            }

            var labels = _profile.Labels;
            DrawToolbar(labels);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawGroups(labels);
                DrawAssets(labels);
                DrawDetails(labels);
            }
        }

        private void DrawToolbar(DataAuthoringWindowLabels labels)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_profile.Title, EditorStyles.boldLabel, GUILayout.Width(180f));
                if (GUILayout.Button(labels.Refresh, EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    Repaint();
                }

                if (GUILayout.Button(labels.ValidateAll, EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    ValidateAll();
                }

                if (GUILayout.Button(labels.ImportPreview, EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    RunImportPreview();
                }

                using (new EditorGUI.DisabledScope(_importPreview == null || _importPreview.HasBlockingErrors))
                {
                    if (GUILayout.Button(labels.ApplyImport, EditorStyles.toolbarButton, GUILayout.Width(96f)))
                    {
                        ApplyImportPreview();
                    }
                }

                if (!string.IsNullOrWhiteSpace(_profile.Actions.OpenIssueDashboardLabel)
                    && GUILayout.Button(_profile.Actions.OpenIssueDashboardLabel, EditorStyles.toolbarButton, GUILayout.Width(96f)))
                {
                    _profile.Actions.OpenIssueDashboard?.Invoke();
                }
            }
        }

        private void DrawGroups(DataAuthoringWindowLabels labels)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(180f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(labels.Groups, EditorStyles.boldLabel);
                foreach (var adapter in _profile.Adapters)
                {
                    var selected = adapter == _selectedAdapter;
                    if (GUILayout.Toggle(selected, adapter.DisplayName, EditorStyles.miniButton) != selected)
                    {
                        _selectedAdapter = adapter;
                        _selectedRecord = null;
                    }
                }
            }
        }

        private void DrawAssets(DataAuthoringWindowLabels labels)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(280f), GUILayout.ExpandHeight(true)))
            {
                EditorGUILayout.LabelField(labels.Assets, EditorStyles.boldLabel);
                _assetScroll = EditorGUILayout.BeginScrollView(_assetScroll);
                foreach (var record in _selectedAdapter?.GetAssets() ?? Array.Empty<DataAuthoringAssetRecord>())
                {
                    var selected = Equals(record, _selectedRecord);
                    var label = string.IsNullOrWhiteSpace(record.DisplayName) ? record.StableId : record.DisplayName;
                    if (GUILayout.Toggle(selected, label, EditorStyles.miniButton) != selected)
                    {
                        _selectedRecord = record;
                        Selection.activeObject = record.Asset;
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawDetails(DataAuthoringWindowLabels labels)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandHeight(true)))
            {
                var asset = _selectedRecord?.Asset;
                if (asset == null)
                {
                    EditorGUILayout.HelpBox(labels.SelectAsset, MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(asset.name, EditorStyles.boldLabel);
                    if (GUILayout.Button(labels.Ping, GUILayout.Width(64f)))
                    {
                        EditorGUIUtility.PingObject(asset);
                    }
                }

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawPreviewAndSections(asset);
                _selectedAdapter?.DrawInspector(asset);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPreviewAndSections(Object asset)
        {
            var context = new DataAuthoringPreviewContext(_profile, _selectedAdapter, asset);
            foreach (var provider in _profile.PreviewProviders.Where(provider => provider.CanPreview(asset)))
            {
                provider.DrawPreview(context);
            }

            foreach (var section in _profile.DetailSections.Where(section => section.CanDraw(asset)))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(section.Title, EditorStyles.boldLabel);
                section.DrawSection(context);
            }
        }

        private void ValidateAll()
        {
            var issueCount = _profile.Adapters
                .SelectMany(adapter => (adapter.GetAssets() ?? Array.Empty<DataAuthoringAssetRecord>())
                    .SelectMany(record => adapter.Validate(record.Asset) ?? Array.Empty<DataAuthoringIssue>()))
                .Count();
            ShowNotification(new GUIContent(string.Format(_profile.Labels.IssueSummaryFormat, _profile.Title, _profile.Adapters.Count, issueCount, 0, 0)));
        }

        private void RunImportPreview()
        {
            var workbook = new TabularImportWorkbook();
            _importPreview = DataAuthoringImportService.BuildPreview(workbook, _profile.ImportAdapters, createMissingAssets: false);
            ShowNotification(new GUIContent(string.Format(_profile.Labels.ChangesSummaryFormat, _importPreview.Changes.Count, _importPreview.BlockingIssues.Count, 0)));
        }

        private void ApplyImportPreview()
        {
            if (_importPreview == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    _profile.Labels.ApplyImportDialogTitle,
                    string.Format(_profile.Labels.ApplyImportDialogFormat, _importPreview.Changes.Count, _profile.Title),
                    _profile.Labels.Apply,
                    _profile.Labels.Cancel))
            {
                return;
            }

            DataAuthoringImportService.Apply(_importPreview, _profile.ImportAdapters);
            _importPreview = null;
            Repaint();
        }
    }
}
