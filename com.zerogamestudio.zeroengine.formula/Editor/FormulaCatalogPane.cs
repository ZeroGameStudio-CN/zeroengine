using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    internal sealed class FormulaCatalogPane
    {
        private enum IndexOperation
        {
            None,
            Load,
            Rebuild,
        }

        private readonly List<FormulaCatalogWindowRow> rows = new();
        private IReadOnlyList<FormulaCatalogAssetRecord> records = Array.Empty<FormulaCatalogAssetRecord>();
        private FormulaCatalogLookup catalogLookup;
        private FormulaCatalogWindowFilter filter;
        private FormulaAssetScanReport lastScanReport;
        private string searchText = string.Empty;
        private string currentProfileId = string.Empty;
        private string currentFingerprint = string.Empty;
        private string indexTaskFingerprint = string.Empty;
        private string indexStatus = "正在准备公式目录…";
        private Task<FormulaReferenceIndexData> indexTask;
        private FormulaReferenceIndexData currentIndex;
        private IndexOperation indexOperation;
        private string catalogAutoFillFingerprint = string.Empty;
        private bool initialized;
        private bool rebuildRequested;
        private bool forceFullRequested;
        private int observedGeneration;
        private double nextGenerationCheckTime;
        private double nextIndexRepaintTime;
        private float listViewportWidth;
        private Vector2 scrollPosition;
        private GUIStyle wrappedLabelStyle;
        private GUIStyle wrappedMiniLabelStyle;
        private GUIStyle wrappedBoldLabelStyle;
        private GUIStyle warningLabelStyle;
        private GUIStyle errorLabelStyle;

        internal void Draw(
            FormulaEditorProfile profile,
            Action<FormulaAsset> openWorkbench,
            Action repaint)
        {
            if (!IsConfiguredProfile(profile))
            {
                indexStatus = "当前项目尚未接入公式适配器；目录不会扫描项目资源。";
                rows.Clear();
                lastScanReport = null;
                EditorGUILayout.HelpBox(indexStatus, MessageType.Info);
                return;
            }

            EnsureReady(profile);
            PollIndex(profile, repaint);

            EditorGUILayout.HelpBox(indexStatus, IndexMessageType);
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
        }

        internal void RefreshRows()
        {
            var profile = FormulaEditorProfileRegistry.ActiveProfile;
            ReloadCatalog(profile);
        }

        private MessageType IndexMessageType
        {
            get
            {
                if (indexTask != null)
                    return MessageType.Info;
                if (currentIndex == null)
                    return MessageType.Warning;
                return MessageType.None;
            }
        }

        private static bool IsConfiguredProfile(FormulaEditorProfile profile)
        {
            return profile != null
                && (!string.IsNullOrWhiteSpace(profile.DefaultSearchRoot)
                    || profile.Providers.Count > 0);
        }

        private void EnsureReady(FormulaEditorProfile profile)
        {
            var profileId = profile?.ProfileId ?? string.Empty;
            if (!initialized || !string.Equals(currentProfileId, profileId, StringComparison.Ordinal))
            {
                ReloadCatalog(profile);
                return;
            }

            if (EditorApplication.timeSinceStartup < nextGenerationCheckTime)
                return;

            nextGenerationCheckTime = EditorApplication.timeSinceStartup + 0.5d;
            var generation = FormulaReferenceIndexCache.GetGeneration();
            if (generation != observedGeneration)
                ReloadCatalog(profile);
        }

        private void ReloadCatalog(FormulaEditorProfile profile)
        {
            var profileId = profile?.ProfileId ?? string.Empty;
            if (!string.Equals(currentProfileId, profileId, StringComparison.Ordinal))
                catalogAutoFillFingerprint = string.Empty;
            currentProfileId = profileId;
            initialized = true;
            observedGeneration = FormulaReferenceIndexCache.GetGeneration();
            nextGenerationCheckTime = EditorApplication.timeSinceStartup + 0.5d;
            records = CollectFormulaRecords(profile);
            currentFingerprint = FormulaReferenceIndexCache.CreateProfileFingerprint(profile, records);
            var catalog = LoadCatalog(profile);
            if (catalog != null
                && !string.Equals(catalogAutoFillFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                catalogAutoFillFingerprint = currentFingerprint;
                EnsureMissingCatalogEntries(catalog, records);
            }
            catalogLookup = catalog?.CreateLookup();
            currentIndex = null;
            lastScanReport = null;
            rows.Clear();
            rows.AddRange(FormulaCatalogWindowModel.BuildRows(
                records,
                catalogLookup,
                Array.Empty<FormulaAssetReference>(),
                null));
            StartCacheLoad(profile);
        }

        private void StartCacheLoad(FormulaEditorProfile profile)
        {
            if (indexTask != null)
                return;

            var cachePath = FormulaReferenceIndexCache.GetCachePath(profile?.ProfileId);
            indexTaskFingerprint = currentFingerprint;
            indexOperation = IndexOperation.Load;
            indexStatus = "正在读取本机公式索引缓存；目录已可浏览。";
            indexTask = Task.Run(() => FormulaReferenceIndexCache.Load(cachePath));
        }

        private void RequestRebuild(FormulaEditorProfile profile, bool forceFull)
        {
            if (profile == null)
                return;

            if (indexTask != null)
            {
                rebuildRequested = true;
                forceFullRequested |= forceFull;
                indexStatus = forceFull
                    ? "缓存任务完成后将执行完整重建；目录仍可使用。"
                    : "缓存任务完成后将更新索引；目录仍可使用。";
                return;
            }

            var candidatePaths = FormulaReferenceAssetDatabase.CollectCandidateAssetPaths(profile);
            var formulaGuids = records.Select(record => record.FormulaGuid).ToArray();
            var generation = FormulaReferenceIndexCache.GetGeneration();
            var fingerprint = currentFingerprint;
            var cached = forceFull ? null : currentIndex;
            var cachePath = FormulaReferenceIndexCache.GetCachePath(profile.ProfileId);
            indexTaskFingerprint = fingerprint;
            indexOperation = IndexOperation.Rebuild;
            indexStatus = forceFull
                ? $"正在后台完整重建索引（{candidatePaths.Count} 个候选文档）；目录仍可使用。"
                : $"正在后台增量更新索引（{candidatePaths.Count} 个候选文档）；目录仍可使用。";
            indexTask = Task.Run(() =>
            {
                var snapshots = FormulaReferenceAssetDatabase.CollectFileSnapshots(candidatePaths);
                var rebuilt = FormulaReferenceIndexCache.Build(
                    generation,
                    fingerprint,
                    snapshots,
                    formulaGuids,
                    cached,
                    forceFull,
                    File.ReadAllText);
                FormulaReferenceIndexCache.Save(cachePath, rebuilt);
                return rebuilt;
            });
        }

        private void PollIndex(FormulaEditorProfile profile, Action repaint)
        {
            if (indexTask == null)
                return;

            if (!indexTask.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup >= nextIndexRepaintTime)
                {
                    nextIndexRepaintTime = EditorApplication.timeSinceStartup + 0.1d;
                    repaint?.Invoke();
                }
                return;
            }

            var completedTask = indexTask;
            var completedOperation = indexOperation;
            var completedFingerprint = indexTaskFingerprint;
            indexTask = null;
            indexOperation = IndexOperation.None;
            indexTaskFingerprint = string.Empty;

            if (completedTask.IsFaulted)
            {
                var message = completedTask.Exception?.GetBaseException().Message ?? "未知错误";
                if (completedOperation == IndexOperation.Load)
                {
                    indexStatus = $"本机索引缓存不可用：{message}。正在后台自动重建，目录仍可浏览。";
                    RequestRebuild(profile, true);
                }
                else
                {
                    indexStatus = $"公式索引自动更新失败：{message}。目录仍可浏览；资源变化后会自动重试。";
                }
                repaint?.Invoke();
                return;
            }

            if (!string.Equals(completedFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                StartCacheLoad(profile);
                return;
            }

            var result = completedTask.Result;
            if (result != null
                && string.Equals(result.ProfileFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                currentIndex = result;
                ApplyIndex(profile, result);
            }

            if (completedOperation == IndexOperation.Load)
            {
                var generation = FormulaReferenceIndexCache.GetGeneration();
                if (FormulaReferenceIndexCache.IsCurrent(result, generation, currentFingerprint))
                {
                    observedGeneration = generation;
                    indexStatus = CreateReadyStatus(result);
                }
                else
                {
                    indexStatus = result == null
                        ? "尚无公式索引，正在后台创建；目录已可浏览。"
                        : "索引已过期，正在后台增量更新；当前显示上次结果。";
                    RequestRebuild(profile, false);
                }
            }
            else if (completedOperation == IndexOperation.Rebuild)
            {
                var generation = FormulaReferenceIndexCache.GetGeneration();
                if (FormulaReferenceIndexCache.IsCurrent(result, generation, currentFingerprint))
                {
                    observedGeneration = generation;
                    indexStatus = CreateReadyStatus(result);
                }
                else
                {
                    indexStatus = "索引构建期间项目资源发生变化，正在补充增量更新。";
                    RequestRebuild(profile, false);
                }
            }

            if (rebuildRequested && indexTask == null)
            {
                var forceFull = forceFullRequested;
                rebuildRequested = false;
                forceFullRequested = false;
                RequestRebuild(profile, forceFull);
            }

            repaint?.Invoke();
        }

        private void ApplyIndex(FormulaEditorProfile profile, FormulaReferenceIndexData index)
        {
            var references = index?.CreateReferences() ?? Array.Empty<FormulaAssetReference>();
            var formulaGuidsByPath = records.ToDictionary(
                record => record.AssetPath,
                record => record.FormulaGuid,
                StringComparer.OrdinalIgnoreCase);
            var scanContext = new FormulaAssetScanContext(catalogLookup, references, formulaGuidsByPath);
            lastScanReport = FormulaAssetScanner.ScanRecords(records, profile, scanContext);
            rows.Clear();
            rows.AddRange(FormulaCatalogWindowModel.BuildRows(
                records,
                catalogLookup,
                references,
                lastScanReport));
        }

        private static string CreateReadyStatus(FormulaReferenceIndexData index)
        {
            if (index == null)
                return "尚无可用公式索引。";

            var updated = index.UpdatedUtcTicks > 0
                ? new DateTime(index.UpdatedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm")
                : "未知时间";
            return $"索引可用 · {index.Documents.Count} 个文档 · 更新于 {updated}。";
        }

        private void DrawSummary(IReadOnlyList<FormulaCatalogWindowRow> visibleRows)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.ScanSummary);
            EditorGUILayout.LabelField(
                FormulaEditorLabels.ScanSummary,
                $"公式={lastScanReport?.AssetCount ?? rows.Count}, 错误={lastScanReport?.ErrorCount ?? 0}, 提醒={lastScanReport?.WarningCount ?? 0}, 显示={visibleRows.Count}");
        }

        private void DrawRows(IReadOnlyList<FormulaCatalogWindowRow> visibleRows, Action<FormulaAsset> openWorkbench)
        {
            FormulaEditorGUILayout.DrawSectionHeader(FormulaEditorLabels.FormulaList);
            if (visibleRows.Count == 0)
            {
                EditorGUILayout.HelpBox(FormulaEditorLabels.NoRows, MessageType.Info);
                return;
            }

            var widthProbe = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.Height(0f),
                GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && widthProbe.width > 0f)
                listViewportWidth = widthProbe.width;

            var compact = listViewportWidth > 0f
                ? listViewportWidth < 620f
                : EditorGUIUtility.currentViewWidth < 820f;

            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar);
            foreach (FormulaCatalogWindowRow row in visibleRows)
                DrawRow(row, compact, openWorkbench);
            GUILayout.EndScrollView();
        }

        private void DrawRow(
            FormulaCatalogWindowRow row,
            bool compact,
            Action<FormulaAsset> openWorkbench)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawRowHeader(row, compact);
            EditorGUILayout.LabelField(row.AssetPath, WrappedMiniLabelStyle);
            if (!string.IsNullOrWhiteSpace(row.Purpose))
                EditorGUILayout.LabelField($"用途  {row.Purpose}", WrappedLabelStyle);
            DrawCatalogMetadata(row);

            foreach (FormulaAssetScanIssue issue in row.Issues)
                DrawIssue(issue);
            EditorGUILayout.EndVertical();

            var cardRect = GUILayoutUtility.GetLastRect();
            if (Selection.activeObject == row.Formula && Event.current.type == EventType.Repaint)
            {
                var indicatorRect = new Rect(cardRect.x + 1f, cardRect.y + 1f, 3f, Mathf.Max(0f, cardRect.height - 2f));
                EditorGUI.DrawRect(indicatorRect, new Color(0.25f, 0.62f, 1f, 1f));
            }

            if (row.Formula == null)
                return;

            GUI.Label(
                cardRect,
                new GUIContent(string.Empty, FormulaEditorLabels.FormulaCardTooltip),
                GUIStyle.none);
            EditorGUIUtility.AddCursorRect(cardRect, MouseCursor.Link);

            var currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown
                || currentEvent.button != 0
                || !cardRect.Contains(currentEvent.mousePosition))
                return;

            SelectFormula(row);
            if (currentEvent.clickCount >= 2)
                openWorkbench?.Invoke(row.Formula);
            currentEvent.Use();
        }

        private void DrawRowHeader(FormulaCatalogWindowRow row, bool compact)
        {
            var referenceSummary = $"{FormulaEditorLabels.References} {row.ReferenceCount}";
            var issueSummary = FormulaEditorLabels.IssueSummary(row.ErrorCount, row.WarningCount, row.InfoCount);
            var catalogStatus = row.HasCatalogEntry
                ? FormulaEditorLabels.CatalogStatusName(row.Status)
                : FormulaEditorLabels.MissingCatalog;

            if (compact)
            {
                EditorGUILayout.LabelField(row.Title, WrappedBoldLabelStyle);
                EditorGUILayout.LabelField(
                    $"{referenceSummary}    {issueSummary}    {catalogStatus}",
                    WrappedMiniLabelStyle);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    row.Title,
                    EditorStyles.boldLabel,
                    GUILayout.MinWidth(140f),
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField(referenceSummary, EditorStyles.miniLabel, GUILayout.Width(72f));
                EditorGUILayout.LabelField(issueSummary, EditorStyles.miniLabel, GUILayout.Width(132f));
                EditorGUILayout.LabelField(catalogStatus, EditorStyles.miniLabel, GUILayout.Width(72f));
            }
        }

        private void DrawIssue(FormulaAssetScanIssue issue)
        {
            var style = issue.Severity == FormulaAssetScanSeverity.Error
                ? ErrorLabelStyle
                : issue.Severity == FormulaAssetScanSeverity.Warning
                    ? WarningLabelStyle
                    : WrappedMiniLabelStyle;
            EditorGUILayout.LabelField(
                $"{FormulaEditorLabels.ScanSeverityName(issue.Severity)} · {issue.Message}",
                style);
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

        private static FormulaCatalog LoadCatalog(FormulaEditorProfile profile)
        {
            return string.IsNullOrEmpty(profile?.CatalogAssetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<FormulaCatalog>(profile.CatalogAssetPath);
        }

        private static int EnsureMissingCatalogEntries(
            FormulaCatalog catalog,
            IReadOnlyList<FormulaCatalogAssetRecord> records)
        {
            if (catalog == null || records == null || records.Count == 0)
                return 0;

            var lookup = catalog.CreateLookup();
            IEnumerable<FormulaCatalogEntry> candidates = records
                .Where(record => record != null
                    && !lookup.TryGetEntry(record.Formula, record.FormulaGuid, out _))
                .Select(record => FormulaCatalogWindowModel.CreateDraftEntry(
                    record.Formula,
                    record.FormulaGuid,
                    record.AssetPath));
            int added = catalog.AddMissingEntries(candidates);
            if (added > 0)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            return added;
        }

        private static void SelectFormula(FormulaCatalogWindowRow row)
        {
            if (row?.Formula == null)
                return;
            Selection.activeObject = row.Formula;
            EditorGUIUtility.PingObject(row.Formula);
        }

        private void DrawCatalogMetadata(FormulaCatalogWindowRow row)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Unit))
                parts.Add($"单位: {row.Unit}");
            if (row.Tags.Count > 0)
                parts.Add($"标签: {string.Join(", ", row.Tags)}");
            if (parts.Count > 0)
                EditorGUILayout.LabelField(string.Join("    ", parts), WrappedMiniLabelStyle);
        }

        private GUIStyle WrappedLabelStyle
        {
            get
            {
                if (wrappedLabelStyle == null)
                    wrappedLabelStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
                return wrappedLabelStyle;
            }
        }

        private GUIStyle WrappedMiniLabelStyle
        {
            get
            {
                if (wrappedMiniLabelStyle == null)
                    wrappedMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                return wrappedMiniLabelStyle;
            }
        }

        private GUIStyle WrappedBoldLabelStyle
        {
            get
            {
                if (wrappedBoldLabelStyle == null)
                    wrappedBoldLabelStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
                return wrappedBoldLabelStyle;
            }
        }

        private GUIStyle WarningLabelStyle
        {
            get
            {
                if (warningLabelStyle == null)
                {
                    warningLabelStyle = new GUIStyle(WrappedMiniLabelStyle);
                    warningLabelStyle.normal.textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 0.76f, 0.32f)
                        : new Color(0.58f, 0.36f, 0.02f);
                }
                return warningLabelStyle;
            }
        }

        private GUIStyle ErrorLabelStyle
        {
            get
            {
                if (errorLabelStyle == null)
                {
                    errorLabelStyle = new GUIStyle(WrappedMiniLabelStyle);
                    errorLabelStyle.normal.textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 0.42f, 0.36f)
                        : new Color(0.72f, 0.08f, 0.05f);
                }
                return errorLabelStyle;
            }
        }
    }
}
