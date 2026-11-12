using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public class DataAuthoringBatchChange
    {
        public DataAuthoringBatchChange(
            Object asset,
            string actionKind,
            string groupName,
            string assetPath,
            string stableId,
            string fieldPath,
            string oldValue,
            string newValue)
        {
            Asset = asset;
            ActionKind = actionKind ?? string.Empty;
            GroupName = groupName ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        public Object Asset { get; }
        public string ActionKind { get; }
        public string GroupName { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string OldValue { get; }
        public string NewValue { get; }
    }

    public sealed class DataAuthoringBatchPreviewResult
    {
        public DataAuthoringBatchPreviewResult(
            IReadOnlyList<DataAuthoringBatchChange> changes,
            IReadOnlyList<DataAuthoringIssue> blockingIssues)
        {
            Changes = changes ?? Array.Empty<DataAuthoringBatchChange>();
            BlockingIssues = blockingIssues ?? Array.Empty<DataAuthoringIssue>();
        }

        public IReadOnlyList<DataAuthoringBatchChange> Changes { get; }
        public IReadOnlyList<DataAuthoringIssue> BlockingIssues { get; }
        public bool CanApply => Changes.Count > 0 && BlockingIssues.Count == 0;
    }

    public sealed class DataAuthoringBatchApplyResult
    {
        public DataAuthoringBatchApplyResult(
            IReadOnlyList<DataAuthoringBatchChange> appliedChanges,
            IReadOnlyList<DataAuthoringBatchChange> skippedChanges,
            IReadOnlyList<DataAuthoringIssue> blockingIssues)
        {
            AppliedChanges = appliedChanges ?? Array.Empty<DataAuthoringBatchChange>();
            SkippedChanges = skippedChanges ?? Array.Empty<DataAuthoringBatchChange>();
            BlockingIssues = blockingIssues ?? Array.Empty<DataAuthoringIssue>();
        }

        public IReadOnlyList<DataAuthoringBatchChange> AppliedChanges { get; }
        public IReadOnlyList<DataAuthoringBatchChange> SkippedChanges { get; }
        public IReadOnlyList<DataAuthoringIssue> BlockingIssues { get; }
    }

    public sealed class DataAuthoringBatchReportLabels
    {
        public DataAuthoringBatchReportLabels(
            string previewStatus,
            string blockingStatus,
            string appliedStatus,
            string skippedStatus,
            string skippedMessage)
        {
            PreviewStatus = previewStatus ?? string.Empty;
            BlockingStatus = blockingStatus ?? string.Empty;
            AppliedStatus = appliedStatus ?? string.Empty;
            SkippedStatus = skippedStatus ?? string.Empty;
            SkippedMessage = skippedMessage ?? string.Empty;
        }

        public string PreviewStatus { get; }
        public string BlockingStatus { get; }
        public string AppliedStatus { get; }
        public string SkippedStatus { get; }
        public string SkippedMessage { get; }
    }

    public sealed class DataAuthoringBatchReportExport
    {
        public DataAuthoringBatchReportExport(string path, string actionKind, int issueCount, int changeCount)
        {
            Path = path ?? string.Empty;
            ActionKind = actionKind ?? string.Empty;
            IssueCount = issueCount;
            ChangeCount = changeCount;
        }

        public string Path { get; }
        public string ActionKind { get; }
        public int IssueCount { get; }
        public int ChangeCount { get; }
    }

    public sealed class DataAuthoringBatchWindowOptions
    {
        public string Title { get; private set; } = "Batch Preview";
        public string ApplyButtonText { get; private set; } = "Apply";
        public string EmptyMessage { get; set; } = "No changes.";
        public string SummaryFormat { get; set; } = "Changes {0} / Blocking {1}";
        public string BlockingMessage { get; set; } = "Fix blocking errors before applying.";
        public string LastApplyFormat { get; set; } = "Last apply {0} / {1} / {2}";
        public string RefreshButtonText { get; set; } = "Refresh";
        public string CopyPreviewButtonText { get; set; } = "Copy Report";
        public string ExportPreviewButtonText { get; set; } = "Export TSV";
        public string CopyApplyButtonText { get; set; } = "Copy Apply Report";
        public string ExportApplyButtonText { get; set; } = "Export Apply TSV";
        public string PreviewExportDialogTitle { get; set; } = "Export Preview TSV";
        public string PreviewExportFilePrefix { get; set; } = "BatchPreview";
        public string ApplyExportDialogTitle { get; set; } = "Export Apply TSV";
        public string ApplyExportFilePrefix { get; set; } = "BatchApply";
        public string ApplyDialogTitle { get; set; } = "Apply Batch";
        public string ApplyDialogFormat { get; set; } = "Apply {0} changes?";
        public string GroupHeader { get; set; } = "Group";
        public string StableIdHeader { get; set; } = "Stable ID";
        public string FieldHeader { get; set; } = "Field";
        public string OldValueHeader { get; set; } = "Old";
        public string NewValueHeader { get; set; } = "New";
        public string ActionHeader { get; set; } = "Action";
        public string PingButtonText { get; set; } = "Ping";
        public string EmptyValueText { get; set; } = "Empty";
        public string PreviewExportActionKind { get; set; } = "BatchPreviewExport";
        public string ApplyExportActionKind { get; set; } = "BatchApplyExport";
        public DataAuthoringBatchReportLabels ReportLabels { get; set; } = new DataAuthoringBatchReportLabels("Preview", "Blocking", "Applied", "Skipped", "Current asset state no longer matches preview");
        public Action<DataAuthoringBatchReportExport> ReportExported { get; set; }
        public Action<string> Notify { get; set; }

        public static DataAuthoringBatchWindowOptions CreateDefault(string title, string applyButtonText)
        {
            return new DataAuthoringBatchWindowOptions
            {
                Title = title ?? "Batch Preview",
                ApplyButtonText = applyButtonText ?? "Apply"
            };
        }
    }

    public sealed class DataAuthoringBatchWindowHost
    {
        private readonly DataAuthoringBatchWindowOptions _options;
        private readonly Func<DataAuthoringBatchPreviewResult> _buildPreview;
        private readonly Func<DataAuthoringBatchPreviewResult, DataAuthoringBatchApplyResult> _applyPreview;
        private DataAuthoringBatchPreviewResult _preview;
        private DataAuthoringBatchApplyResult _lastApplyResult;
        private Vector2 _scroll;

        public DataAuthoringBatchWindowHost(
            DataAuthoringBatchWindowOptions options,
            Func<DataAuthoringBatchPreviewResult> buildPreview,
            Func<DataAuthoringBatchPreviewResult, DataAuthoringBatchApplyResult> applyPreview)
        {
            _options = options ?? DataAuthoringBatchWindowOptions.CreateDefault("Batch Preview", "Apply");
            _buildPreview = buildPreview ?? (() => new DataAuthoringBatchPreviewResult(Array.Empty<DataAuthoringBatchChange>(), Array.Empty<DataAuthoringIssue>()));
            _applyPreview = applyPreview ?? (_ => new DataAuthoringBatchApplyResult(Array.Empty<DataAuthoringBatchChange>(), Array.Empty<DataAuthoringBatchChange>(), Array.Empty<DataAuthoringIssue>()));
        }

        public void RefreshPreview()
        {
            _preview = _buildPreview();
        }

        public void Draw()
        {
            _preview ??= _buildPreview();
            DrawToolbar();
            DrawSummary();
            DrawRows();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_options.RefreshButtonText, EditorStyles.toolbarButton, GUILayout.Width(78f)))
                {
                    RefreshPreview();
                }

                using (new EditorGUI.DisabledScope(_preview == null))
                {
                    if (GUILayout.Button(_options.CopyPreviewButtonText, EditorStyles.toolbarButton, GUILayout.Width(88f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = DataAuthoringBatchReportExporter.CreateTsvReport(_preview, _options.ReportLabels);
                        _options.Notify?.Invoke(_options.CopyPreviewButtonText);
                    }

                    if (GUILayout.Button(_options.ExportPreviewButtonText, EditorStyles.toolbarButton, GUILayout.Width(78f)))
                    {
                        ExportPreview();
                    }
                }

                using (new EditorGUI.DisabledScope(_preview == null || !_preview.CanApply))
                {
                    if (GUILayout.Button(_options.ApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(120f)))
                    {
                        ApplyPreview();
                    }
                }

                if (_lastApplyResult != null)
                {
                    using (new EditorGUI.DisabledScope(false))
                    {
                        if (GUILayout.Button(_options.CopyApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(112f)))
                        {
                            EditorGUIUtility.systemCopyBuffer = DataAuthoringBatchReportExporter.CreateTsvReport(_lastApplyResult, _options.ReportLabels);
                        }

                        if (GUILayout.Button(_options.ExportApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(112f)))
                        {
                            ExportApply();
                        }
                    }
                }
            }
        }

        private void DrawSummary()
        {
            var changeCount = _preview?.Changes.Count ?? 0;
            var issueCount = _preview?.BlockingIssues.Count ?? 0;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(_options.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Format(_options.SummaryFormat, changeCount, issueCount), EditorStyles.miniLabel);
                if (issueCount > 0)
                {
                    EditorGUILayout.HelpBox(_options.BlockingMessage, MessageType.Error);
                }

                if (_lastApplyResult != null)
                {
                    EditorGUILayout.LabelField(
                        string.Format(
                            _options.LastApplyFormat,
                            _lastApplyResult.AppliedChanges.Count,
                            _lastApplyResult.SkippedChanges.Count,
                            _lastApplyResult.BlockingIssues.Count),
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawRows()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_preview == null || (_preview.Changes.Count == 0 && _preview.BlockingIssues.Count == 0))
            {
                EditorGUILayout.HelpBox(_options.EmptyMessage, MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawHeader();
            foreach (var change in _preview.Changes)
            {
                DrawChange(change);
            }

            foreach (var issue in _preview.BlockingIssues)
            {
                DrawIssue(issue);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_options.GroupHeader, EditorStyles.boldLabel, GUILayout.Width(80f));
                GUILayout.Label(_options.StableIdHeader, EditorStyles.boldLabel, GUILayout.Width(140f));
                GUILayout.Label(_options.FieldHeader, EditorStyles.boldLabel, GUILayout.Width(160f));
                GUILayout.Label(_options.OldValueHeader, EditorStyles.boldLabel, GUILayout.Width(120f));
                GUILayout.Label(_options.NewValueHeader, EditorStyles.boldLabel, GUILayout.Width(120f));
                GUILayout.Label(_options.ActionHeader, EditorStyles.boldLabel, GUILayout.Width(70f));
            }
        }

        private void DrawChange(DataAuthoringBatchChange change)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(change.GroupName, GUILayout.Width(80f));
                EditorGUILayout.LabelField(change.StableId, GUILayout.Width(140f));
                EditorGUILayout.LabelField(change.FieldPath, GUILayout.Width(160f));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(change.OldValue) ? _options.EmptyValueText : change.OldValue, GUILayout.Width(120f));
                EditorGUILayout.LabelField(change.NewValue, GUILayout.Width(120f));
                using (new EditorGUI.DisabledScope(change.Asset == null))
                {
                    if (GUILayout.Button(_options.PingButtonText, GUILayout.Width(64f)))
                    {
                        Selection.activeObject = change.Asset;
                        EditorGUIUtility.PingObject(change.Asset);
                    }
                }
            }
        }

        private static void DrawIssue(DataAuthoringIssue issue)
        {
            EditorGUILayout.HelpBox($"{issue.AssetType} / {issue.StableId} / {issue.FieldPath}: {issue.Message}", MessageType.Error);
        }

        private void ApplyPreview()
        {
            if (_preview == null || !_preview.CanApply)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    _options.ApplyDialogTitle,
                    string.Format(_options.ApplyDialogFormat, _preview.Changes.Count),
                    _options.ApplyButtonText,
                    "Cancel"))
            {
                return;
            }

            _lastApplyResult = _applyPreview(_preview);
            RefreshPreview();
        }

        private void ExportPreview()
        {
            ExportReport(
                _options.PreviewExportDialogTitle,
                _options.PreviewExportFilePrefix,
                _options.PreviewExportActionKind,
                DataAuthoringBatchReportExporter.CreateTsvReport(_preview, _options.ReportLabels),
                _preview?.BlockingIssues.Count ?? 0,
                _preview?.Changes.Count ?? 0);
        }

        private void ExportApply()
        {
            ExportReport(
                _options.ApplyExportDialogTitle,
                _options.ApplyExportFilePrefix,
                _options.ApplyExportActionKind,
                DataAuthoringBatchReportExporter.CreateTsvReport(_lastApplyResult, _options.ReportLabels),
                _lastApplyResult?.BlockingIssues.Count ?? 0,
                (_lastApplyResult?.AppliedChanges.Count ?? 0) + (_lastApplyResult?.SkippedChanges.Count ?? 0));
        }

        private void ExportReport(string title, string filePrefix, string actionKind, string report, int issueCount, int changeCount)
        {
            var path = EditorUtility.SaveFilePanel(
                title,
                "Assets",
                $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.tsv",
                "tsv");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            File.WriteAllText(path, report ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _options.ReportExported?.Invoke(new DataAuthoringBatchReportExport(path, actionKind, issueCount, changeCount));
            _options.Notify?.Invoke(path);
        }
    }

    public static class DataAuthoringBatchReportExporter
    {
        public static string CreateTsvReport(DataAuthoringBatchPreviewResult preview, DataAuthoringBatchReportLabels labels)
        {
            var builder = new StringBuilder();
            builder.AppendLine("rowType\tgroup\tassetPath\tstableId\tfieldPath\toldValue\tnewValue\tstatus\tmessage");
            if (preview == null)
            {
                return builder.ToString();
            }

            foreach (var change in preview.Changes)
            {
                AppendRow(
                    builder,
                    "change",
                    change.GroupName,
                    change.AssetPath,
                    change.StableId,
                    change.FieldPath,
                    change.OldValue,
                    change.NewValue,
                    labels.PreviewStatus,
                    string.Empty);
            }

            foreach (var issue in preview.BlockingIssues)
            {
                AppendRow(
                    builder,
                    "blockingIssue",
                    issue.AssetType,
                    issue.AssetPath,
                    issue.StableId,
                    issue.FieldPath,
                    string.Empty,
                    string.Empty,
                    labels.BlockingStatus,
                    issue.Message);
            }

            return builder.ToString();
        }

        public static string CreateTsvReport(DataAuthoringBatchPreviewResult preview)
        {
            return CreateTsvReport(preview, new DataAuthoringBatchReportLabels("Preview", "Blocking", "Applied", "Skipped", "Current asset state no longer matches preview"));
        }

        public static string CreateTsvReport(DataAuthoringBatchApplyResult result, DataAuthoringBatchReportLabels labels)
        {
            var builder = new StringBuilder();
            builder.AppendLine("rowType\tgroup\tassetPath\tstableId\tfieldPath\toldValue\tnewValue\tstatus\tmessage");
            if (result == null)
            {
                return builder.ToString();
            }

            foreach (var change in result.AppliedChanges)
            {
                AppendRow(builder, "appliedChange", change.GroupName, change.AssetPath, change.StableId, change.FieldPath, change.OldValue, change.NewValue, labels.AppliedStatus, string.Empty);
            }

            foreach (var change in result.SkippedChanges)
            {
                AppendRow(builder, "skippedChange", change.GroupName, change.AssetPath, change.StableId, change.FieldPath, change.OldValue, change.NewValue, labels.SkippedStatus, labels.SkippedMessage);
            }

            foreach (var issue in result.BlockingIssues)
            {
                AppendRow(builder, "blockingIssue", issue.AssetType, issue.AssetPath, issue.StableId, issue.FieldPath, string.Empty, string.Empty, labels.BlockingStatus, issue.Message);
            }

            return builder.ToString();
        }

        public static string CreateTsvReport(DataAuthoringBatchApplyResult result)
        {
            return CreateTsvReport(result, new DataAuthoringBatchReportLabels("Preview", "Blocking", "Applied", "Skipped", "Current asset state no longer matches preview"));
        }

        private static void AppendRow(StringBuilder builder, params string[] cells)
        {
            builder.AppendLine(string.Join("\t", cells.Select(SanitizeCell)));
        }

        private static string SanitizeCell(string value)
        {
            return (value ?? string.Empty)
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }
    }
}
