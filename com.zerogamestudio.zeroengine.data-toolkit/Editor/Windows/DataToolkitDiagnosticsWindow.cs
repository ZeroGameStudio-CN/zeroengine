using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataToolkitDiagnosticsWindow : EditorWindow
    {
        private DataToolkitContext context;
        private IReadOnlyList<IDataToolkitAssetInspectorProvider> inspectorProviders = Array.Empty<IDataToolkitAssetInspectorProvider>();
        private DataToolkitDiagnosticsReport report;
        private Vector2 scroll;

        public static void Open(
            DataToolkitContext context,
            IReadOnlyList<IDataToolkitAssetInspectorProvider> inspectorProviders)
        {
            var window = GetWindow<DataToolkitDiagnosticsWindow>();
            window.Initialize(context, inspectorProviders);
            window.Show();
        }

        private void Initialize(
            DataToolkitContext nextContext,
            IReadOnlyList<IDataToolkitAssetInspectorProvider> nextInspectorProviders)
        {
            context = nextContext ?? throw new ArgumentNullException(nameof(nextContext));
            inspectorProviders = nextInspectorProviders ?? Array.Empty<IDataToolkitAssetInspectorProvider>();
            titleContent = new GUIContent($"{context.Settings.ProjectId} Data Diagnostics");
            minSize = new Vector2(720f, 420f);
            RefreshReport();
        }

        private void OnGUI()
        {
            if (context == null)
            {
                EditorGUILayout.HelpBox("Open diagnostics from Data Toolkit so project settings are available.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(titleContent.text, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    RefreshReport();
                }
            }

            if (report == null)
            {
                RefreshReport();
            }

            DrawSummary();
            DrawRows();
        }

        private void RefreshReport()
        {
            ManageableDataTypeDiscovery.ClearCache();
            AssetDiscoveryService.ClearCaches();
            report = DataToolkitDiagnosticsService.BuildReport(context, inspectorProviders);
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
                DrawSummaryRow("Types", report.TypeCount.ToString());
                DrawSummaryRow("Assets", report.AssetCount.ToString());
                DrawSummaryRow("First-class inspectors", report.FirstClassCount.ToString());
                DrawSummaryRow("Safe previews", report.SafePreviewCount.ToString());
                DrawSummaryRow("Raw Odin fallback", report.RawOdinFallbackCount.ToString());
                DrawSummaryRow("No assets", report.NoAssetsCount.ToString());
                DrawSummaryRow("Unsupported", report.UnsupportedCount.ToString());
            }
        }

        private static void DrawSummaryRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(160f));
                GUILayout.Label(value, EditorStyles.boldLabel);
            }
        }

        private void DrawRows()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(190f));
                GUILayout.Label("Assets", EditorStyles.miniBoldLabel, GUILayout.Width(56f));
                GUILayout.Label("Coverage", EditorStyles.miniBoldLabel, GUILayout.Width(130f));
                GUILayout.Label("Sample / Reason", EditorStyles.miniBoldLabel);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in report.Types)
            {
                DrawRow(row);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawRow(DataToolkitTypeCoverageInfo row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(row.TypeName, EditorStyles.boldLabel, GUILayout.Width(190f));
                    GUILayout.Label(row.AssetCount.ToString(), GUILayout.Width(56f));
                    GUILayout.Label(row.CoverageLevel.ToString(), GUILayout.Width(130f));

                    var sample = string.IsNullOrEmpty(row.SampleAssetPath) ? "No sample asset" : row.SampleAssetPath;
                    GUILayout.Label(sample, EditorStyles.miniLabel);
                }

                EditorGUILayout.LabelField(row.Reason, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}
