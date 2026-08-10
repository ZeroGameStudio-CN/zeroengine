using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal sealed class FormulaCatalogPane
    {
        private readonly List<FormulaCatalogWindowRow> rows = new();
        private FormulaCatalogWindowFilter filter;
        private FormulaAssetScanReport lastScanReport;
        private string lastMarkdown = string.Empty;
        private string searchText = string.Empty;
        private Vector2 scrollPosition;

        internal void Draw(FormulaEditorProfile profile, Action<FormulaAsset> openWorkbench)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        FormulaEditorLabels.Refresh,
                        FormulaEditorLabels.RefreshTooltip)))
                    RefreshRows();
                if (GUILayout.Button(new GUIContent(
                        FormulaEditorLabels.Scan,
                        FormulaEditorLabels.ScanTooltip)))
                    RefreshRows();
                if (GUILayout.Button(new GUIContent(
                        FormulaEditorLabels.GenerateMissingCatalogEntries,
                        FormulaEditorLabels.GenerateMissingCatalogEntriesTooltip)))
                    GenerateMissingCatalogEntries(profile);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                searchText = EditorGUILayout.TextField(
                    new GUIContent(FormulaEditorLabels.Search, FormulaEditorLabels.SearchTooltip),
                    searchText);
                filter = FormulaEditorGUILayout.DrawCatalogFilter(filter);
            }

            var visibleRows = FormulaCatalogWindowModel.FilterRows(rows, filter, searchText);
            DrawSummary(visibleRows);
            DrawRows(visibleRows, openWorkbench);
            DrawMarkdown();
        }

        internal void RefreshRows()
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

        private void DrawSummary(IReadOnlyList<FormulaCatalogWindowRow> visibleRows)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.ScanSummary);
            EditorGUILayout.LabelField(
                FormulaEditorLabels.ScanSummary,
                $"公式={lastScanReport?.AssetCount ?? rows.Count}, 错误={lastScanReport?.ErrorCount ?? 0}, 警告={lastScanReport?.WarningCount ?? 0}, 显示={visibleRows.Count}");
        }

        private void DrawRows(IReadOnlyList<FormulaCatalogWindowRow> visibleRows, Action<FormulaAsset> openWorkbench)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.FormulaList);
            if (visibleRows.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoRows, MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (FormulaCatalogWindowRow row in visibleRows)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(row.Title, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField($"{FormulaEditorLabels.References} {row.ReferenceCount}", GUILayout.Width(72f));
                        EditorGUILayout.LabelField(
                            FormulaEditorLabels.IssueSummary(row.ErrorCount, row.WarningCount, row.InfoCount),
                            GUILayout.Width(148f));
                        EditorGUILayout.LabelField(
                            row.HasCatalogEntry ? FormulaEditorLabels.CatalogStatusName(row.Status) : FormulaEditorLabels.MissingCatalog,
                            GUILayout.Width(64f));
                        if (GUILayout.Button(
                                new GUIContent(FormulaEditorLabels.Ping, FormulaEditorLabels.PingTooltip),
                                GUILayout.Width(48f)))
                            Ping(row);
                        if (GUILayout.Button(
                                new GUIContent(
                                    FormulaEditorLabels.OpenWorkbench,
                                    FormulaEditorLabels.OpenWorkbenchTooltip),
                                GUILayout.Width(64f)))
                            openWorkbench?.Invoke(row.Formula);
                    }

                    EditorGUILayout.SelectableLabel(
                        row.AssetPath,
                        EditorStyles.miniLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (!string.IsNullOrWhiteSpace(row.Purpose))
                        EditorGUILayout.LabelField("用途", row.Purpose);
                    DrawCatalogMetadata(row);

                    foreach (FormulaAssetScanIssue issue in row.Issues)
                    {
                        EditorGUILayout.HelpBox(
                            $"{FormulaEditorLabels.ScanSeverityName(issue.Severity)}: {issue.Message}",
                            issue.Severity == FormulaAssetScanSeverity.Error ? MessageType.Error : MessageType.Warning);
                    }
                }
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

        private static IReadOnlyList<FormulaCatalogAssetRecord> CollectFormulaRecords(FormulaEditorProfile profile)
        {
            string root = string.IsNullOrEmpty(profile?.DefaultSearchRoot) ? "Assets" : profile.DefaultSearchRoot;
            return AssetDatabase.FindAssets("t:FormulaAsset", new[] { root })
                .Select(guid =>
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var formula = AssetDatabase.LoadAssetAtPath<FormulaAsset>(path);
                    string displayName = formula != null ? formula.FormulaName : Path.GetFileNameWithoutExtension(path);
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

            IReadOnlyList<FormulaReferenceTextDocument> documents = FormulaReferenceAssetDatabase.CollectTextDocuments(profile);
            var options = new FormulaReferenceSearchOptions(profile.ReferenceRoots, profile.ExcludedReferenceRoots);
            var references = new List<FormulaAssetReference>();
            foreach (FormulaCatalogAssetRecord record in records)
                references.AddRange(FormulaReferenceIndexer.FindGuidReferences(record.FormulaGuid, documents, options));
            return references;
        }

        private static FormulaCatalog LoadCatalog(FormulaEditorProfile profile)
        {
            return string.IsNullOrEmpty(profile?.CatalogAssetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<FormulaCatalog>(profile.CatalogAssetPath);
        }

        private void GenerateMissingCatalogEntries(FormulaEditorProfile profile)
        {
            if (string.IsNullOrEmpty(profile?.CatalogAssetPath))
            {
                Debug.LogWarning("[Formula] 当前 profile 未配置 Catalog 路径。");
                return;
            }

            FormulaCatalog catalog = LoadCatalog(profile) ?? CreateCatalogAsset(profile.CatalogAssetPath);
            IReadOnlyList<FormulaCatalogAssetRecord> records = CollectFormulaRecords(profile);
            IEnumerable<FormulaCatalogEntry> candidates = records.Select(record =>
                FormulaCatalogWindowModel.CreateDraftEntry(record.Formula, record.FormulaGuid, record.AssetPath));
            int added = catalog.AddMissingEntries(candidates);
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
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), directory));
                AssetDatabase.Refresh();
            }

            var catalog = ScriptableObject.CreateInstance<FormulaCatalog>();
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
