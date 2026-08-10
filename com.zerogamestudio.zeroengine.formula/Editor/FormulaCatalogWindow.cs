using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class FormulaCatalogWindow : EditorWindow
    {
        private readonly List<FormulaCatalogWindowRow> rows = new();
        private FormulaCatalogWindowFilter filter;
        private FormulaAssetScanReport lastScanReport;
        private string lastMarkdown = string.Empty;
        private string searchText = string.Empty;
        private Vector2 scrollPosition;

        [MenuItem("ZeroEngine/Formula/Formula Catalog", priority = 129)]
        private static void Open()
        {
            OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
        }

        public static void OpenWithProfile(FormulaEditorProfile profile)
        {
            if (profile != null)
            {
                var registered = FormulaEditorProfileRegistry.RegisteredProfiles
                    .Any(registeredProfile => registeredProfile.ProfileId == profile.ProfileId);
                if (!registered)
                    FormulaEditorProfileRegistry.Register(profile);

                FormulaEditorProfileRegistry.SetActiveProfile(profile.ProfileId);
            }

            GetWindow<FormulaCatalogWindow>("公式目录").Show();
        }

        private void OnEnable()
        {
            RefreshRows();
        }

        private void OnGUI()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            var root = string.IsNullOrEmpty(profile.DefaultSearchRoot) ? "Assets" : profile.DefaultSearchRoot;
            var catalogPath = string.IsNullOrEmpty(profile.CatalogAssetPath)
                ? FormulaEditorLabels.NoCatalogPath
                : profile.CatalogAssetPath;
            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                "公式目录",
                $"{profile.DisplayName} ({profile.ProfileId}) · {FormulaEditorLabels.FormulaRoot}: {root} · {FormulaEditorLabels.Catalog}: {catalogPath}");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(FormulaEditorLabels.Refresh))
                RefreshRows();
            if (GUILayout.Button(FormulaEditorLabels.Scan))
                RefreshRows();
            if (GUILayout.Button(FormulaEditorLabels.GenerateMissingCatalogEntries))
                GenerateMissingCatalogEntries(profile);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            searchText = EditorGUILayout.TextField(FormulaEditorLabels.Search, searchText);
            filter = FormulaEditorGUILayout.DrawCatalogFilter(filter);
            EditorGUILayout.EndVertical();

            var visibleRows = FormulaCatalogWindowModel.FilterRows(rows, filter, searchText);
            DrawSummary(visibleRows);
            DrawRows(profile, visibleRows);
            DrawMarkdown();
        }

        private void DrawSummary(IReadOnlyList<FormulaCatalogWindowRow> visibleRows)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.ScanSummary);
            EditorGUILayout.LabelField(
                FormulaEditorLabels.ScanSummary,
                $"公式={lastScanReport?.AssetCount ?? rows.Count}, 错误={lastScanReport?.ErrorCount ?? 0}, 警告={lastScanReport?.WarningCount ?? 0}, 显示={visibleRows.Count}");
        }

        private void DrawRows(FormulaEditorProfile profile, IReadOnlyList<FormulaCatalogWindowRow> visibleRows)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.FormulaList);
            if (visibleRows.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoRows, MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var row in visibleRows)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(row.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{FormulaEditorLabels.References} {row.ReferenceCount}", GUILayout.Width(72f));
                EditorGUILayout.LabelField(
                    FormulaEditorLabels.IssueSummary(row.ErrorCount, row.WarningCount, row.InfoCount),
                    GUILayout.Width(148f));
                EditorGUILayout.LabelField(
                    row.HasCatalogEntry ? FormulaEditorLabels.CatalogStatusName(row.Status) : FormulaEditorLabels.MissingCatalog,
                    GUILayout.Width(64f));
                if (GUILayout.Button(FormulaEditorLabels.Ping, GUILayout.Width(48f)))
                    Ping(row);
                if (GUILayout.Button(FormulaEditorLabels.OpenWorkbench, GUILayout.Width(64f)))
                    FormulaWorkbenchWindow.OpenWithFormula(profile, row.Formula);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.SelectableLabel(row.AssetPath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (!string.IsNullOrWhiteSpace(row.Purpose))
                    EditorGUILayout.LabelField("用途", row.Purpose);
                DrawCatalogMetadata(row);

                foreach (var issue in row.Issues)
                    EditorGUILayout.HelpBox(
                        $"{FormulaEditorLabels.ScanSeverityName(issue.Severity)}: {issue.Message}",
                        issue.Severity == FormulaAssetScanSeverity.Error ? MessageType.Error : MessageType.Warning);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMarkdown()
        {
            if (string.IsNullOrEmpty(lastMarkdown))
                return;

            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.PreviewReportMarkdown);
            EditorGUILayout.TextArea(lastMarkdown, GUILayout.MinHeight(72f));
        }

        private void RefreshRows()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            var records = CollectFormulaRecords(profile);
            var catalog = LoadCatalog(profile);
            var references = CollectReferences(profile, records);

            lastScanReport = FormulaAssetScanner.Scan(profile);
            lastMarkdown = FormulaAssetScanReportExporter.ToMarkdown(lastScanReport);
            rows.Clear();
            rows.AddRange(FormulaCatalogWindowModel.BuildRows(
                records,
                catalog?.CreateLookup(),
                references,
                lastScanReport));
        }

        private static IReadOnlyList<FormulaCatalogAssetRecord> CollectFormulaRecords(FormulaEditorProfile profile)
        {
            var root = string.IsNullOrEmpty(profile?.DefaultSearchRoot) ? "Assets" : profile.DefaultSearchRoot;
            return AssetDatabase.FindAssets("t:FormulaAsset", new[] { root })
                .Select(guid =>
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                    var displayName = formula != null ? formula.FormulaName : Path.GetFileNameWithoutExtension(path);
                    return new FormulaCatalogAssetRecord(path, guid, displayName, formula);
                })
                .Where(record => !string.IsNullOrEmpty(record.AssetPath))
                .ToList();
        }

        private static IReadOnlyList<FormulaAssetReference> CollectReferences(
            FormulaEditorProfile profile,
            IReadOnlyList<FormulaCatalogAssetRecord> records)
        {
            if (profile == null || records == null || records.Count == 0)
                return Array.Empty<FormulaAssetReference>();

            var documents = FormulaReferenceAssetDatabase.CollectTextDocuments(profile);
            var options = new FormulaReferenceSearchOptions(profile.ReferenceRoots, profile.ExcludedReferenceRoots);
            var references = new List<FormulaAssetReference>();
            foreach (var record in records)
                references.AddRange(FormulaReferenceIndexer.FindGuidReferences(record.FormulaGuid, documents, options));

            return references;
        }

        private static FormulaCatalog LoadCatalog(FormulaEditorProfile profile)
        {
            if (string.IsNullOrEmpty(profile?.CatalogAssetPath))
                return null;

            return AssetDatabase.LoadAssetAtPath<FormulaCatalog>(profile.CatalogAssetPath);
        }

        private void GenerateMissingCatalogEntries(FormulaEditorProfile profile)
        {
            if (string.IsNullOrEmpty(profile?.CatalogAssetPath))
            {
                Debug.LogWarning("[Formula] 当前 profile 未配置 Catalog 路径。");
                return;
            }

            var catalog = LoadCatalog(profile);
            if (catalog == null)
                catalog = CreateCatalogAsset(profile.CatalogAssetPath);

            var records = CollectFormulaRecords(profile);
            var candidates = records.Select(record =>
                FormulaCatalogWindowModel.CreateDraftEntry(record.Formula, record.FormulaGuid, record.AssetPath));
            var added = catalog.AddMissingEntries(candidates);
            if (added > 0)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[Formula] 已生成缺失目录项：{added}");
            RefreshRows();
        }

        private static FormulaCatalog CreateCatalogAsset(string assetPath)
        {
            var directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory))
            {
                var fullDirectory = Path.Combine(Directory.GetCurrentDirectory(), directory);
                Directory.CreateDirectory(fullDirectory);
                AssetDatabase.Refresh();
            }

            var catalog = CreateInstance<FormulaCatalog>();
            AssetDatabase.CreateAsset(catalog, assetPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static void Ping(FormulaCatalogWindowRow row)
        {
            if (row?.Formula == null)
                return;

            Selection.activeObject = row.Formula;
            EditorGUIUtility.PingObject(row.Formula);
        }

        private static void DrawCatalogMetadata(FormulaCatalogWindowRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Owner))
                parts.Add($"负责人: {row.Owner}");
            if (!string.IsNullOrWhiteSpace(row.Unit))
                parts.Add($"单位: {row.Unit}");
            if (row.Tags.Count > 0)
                parts.Add($"标签: {string.Join(", ", row.Tags)}");

            if (parts.Count > 0)
                EditorGUILayout.LabelField(string.Join("    ", parts), EditorStyles.miniLabel);
        }
    }
}
