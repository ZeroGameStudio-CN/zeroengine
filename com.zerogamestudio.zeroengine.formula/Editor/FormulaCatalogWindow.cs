using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public sealed class FormulaCatalogWindow : EditorWindow
    {
        private readonly List<FormulaCatalogWindowRow> rows = new();
        private FormulaCatalogWindowFilter filter;
        private FormulaAssetScanReport lastScanReport;
        private string lastMarkdown = string.Empty;
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
            EditorGUILayout.LabelField("Profile", $"{profile.DisplayName} ({profile.ProfileId})");
            EditorGUILayout.LabelField("公式根目录", string.IsNullOrEmpty(profile.DefaultSearchRoot) ? "Assets" : profile.DefaultSearchRoot);
            EditorGUILayout.LabelField("Catalog", string.IsNullOrEmpty(profile.CatalogAssetPath) ? "<未配置>" : profile.CatalogAssetPath);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新"))
                RefreshRows();
            if (GUILayout.Button("扫描"))
                RefreshRows();
            if (GUILayout.Button("生成缺失目录项"))
                GenerateMissingCatalogEntries(profile);
            EditorGUILayout.EndHorizontal();

            filter = (FormulaCatalogWindowFilter)EditorGUILayout.EnumPopup("筛选", filter);
            DrawSummary();
            DrawRows(profile);
            DrawMarkdown();
        }

        private void DrawSummary()
        {
            var visibleRows = rows.Where(row => FormulaCatalogWindowModel.MatchesFilter(row, filter)).ToList();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "扫描摘要",
                $"公式={lastScanReport?.AssetCount ?? rows.Count}, 错误={lastScanReport?.ErrorCount ?? 0}, 警告={lastScanReport?.WarningCount ?? 0}, 显示={visibleRows.Count}");
        }

        private void DrawRows(FormulaEditorProfile profile)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("公式", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var row in rows.Where(row => FormulaCatalogWindowModel.MatchesFilter(row, filter)))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(row.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"引用 {row.ReferenceCount}", GUILayout.Width(64f));
                EditorGUILayout.LabelField($"E{row.ErrorCount}/W{row.WarningCount}", GUILayout.Width(72f));
                EditorGUILayout.LabelField(row.HasCatalogEntry ? row.Status.ToString() : "缺目录", GUILayout.Width(72f));
                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                    Ping(row);
                if (GUILayout.Button("Workbench", GUILayout.Width(88f)))
                    FormulaWorkbenchWindow.OpenWithProfile(profile);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(row.AssetPath);
                if (!string.IsNullOrWhiteSpace(row.Purpose))
                    EditorGUILayout.LabelField("用途", row.Purpose);

                foreach (var issue in row.Issues)
                    EditorGUILayout.LabelField(issue.Severity.ToString(), issue.Message);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawMarkdown()
        {
            if (string.IsNullOrEmpty(lastMarkdown))
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Markdown 报告", EditorStyles.boldLabel);
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
    }
}
