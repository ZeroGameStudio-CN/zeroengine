using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringBatchReportExport
    {
        public DataAuthoringBatchReportExport(
            string path,
            string actionKind,
            int issueCount,
            int changeCount,
            bool isApplyReport)
        {
            Path = path ?? string.Empty;
            ActionKind = actionKind ?? string.Empty;
            IssueCount = Mathf.Max(0, issueCount);
            ChangeCount = Mathf.Max(0, changeCount);
            IsApplyReport = isApplyReport;
        }

        public string Path { get; }
        public string ActionKind { get; }
        public int IssueCount { get; }
        public int ChangeCount { get; }
        public bool IsApplyReport { get; }
    }

    public sealed class DataAuthoringBatchWindowOptions
    {
        public static DataAuthoringBatchWindowOptions CreateDefault(string summaryTitle, string applyButtonText)
        {
            return new DataAuthoringBatchWindowOptions
            {
                SummaryTitle = string.IsNullOrWhiteSpace(summaryTitle) ? "Batch Preview" : summaryTitle,
                ApplyButtonText = string.IsNullOrWhiteSpace(applyButtonText) ? "Apply" : applyButtonText
            };
        }

        public string SummaryTitle { get; set; } = "Batch Preview";
        public string EmptyMessage { get; set; } = "No batch changes.";
        public string SummaryFormat { get; set; } = "{0} changes / {1} blocking issues.";
        public string BlockingMessage { get; set; } = "Blocking issues must be fixed before apply.";
        public string LastApplyFormat { get; set; } = "Last apply: {0} applied / {1} skipped / {2} blocked";
        public string RefreshButtonText { get; set; } = "Refresh";
        public string CopyPreviewButtonText { get; set; } = "Copy Report";
        public string ExportPreviewButtonText { get; set; } = "Export TSV";
        public string CopyApplyButtonText { get; set; } = "Copy Apply Report";
        public string ExportApplyButtonText { get; set; } = "Export Apply TSV";
        public string ApplyButtonText { get; set; } = "Apply";
        public string PreviewExportDialogTitle { get; set; } = "Export Batch Preview TSV";
        public string PreviewExportFilePrefix { get; set; } = "DataAuthoringBatchPreview";
        public string ApplyExportDialogTitle { get; set; } = "Export Batch Apply TSV";
        public string ApplyExportFilePrefix { get; set; } = "DataAuthoringBatchApply";
        public string ApplyDialogTitle { get; set; } = "Apply Batch";
        public string ApplyDialogFormat { get; set; } = "Apply {0} batch changes?";
        public string GroupHeader { get; set; } = "Group";
        public string StableIdHeader { get; set; } = "Stable ID";
        public string FieldHeader { get; set; } = "Field";
        public string OldValueHeader { get; set; } = "Old Value";
        public string NewValueHeader { get; set; } = "New Value";
        public string ActionHeader { get; set; } = "Action";
        public string PingButtonText { get; set; } = "Ping";
        public string EmptyValueText { get; set; } = "Empty";
        public string PreviewExportActionKind { get; set; } = "BatchPreviewExport";
        public string ApplyExportActionKind { get; set; } = "BatchApplyExport";
        public DataAuthoringBatchReportLabels ReportLabels { get; set; } = DataAuthoringBatchReportLabels.Default;
        public Action<DataAuthoringBatchReportExport> ReportExported { get; set; }
        public Action<string> Notify { get; set; }
    }

    public sealed class DataAuthoringBatchWindowHost
    {
        private static readonly DataAuthoringBatchPreviewResult EmptyPreview = new(
            Array.Empty<DataAuthoringBatchChange>(),
            Array.Empty<DataAuthoringIssue>());

        private static readonly DataAuthoringBatchApplyResult EmptyApplyResult = new(
            Array.Empty<DataAuthoringBatchChange>(),
            Array.Empty<DataAuthoringBatchChange>(),
            Array.Empty<DataAuthoringIssue>());

        private readonly DataAuthoringBatchWindowOptions _options;
        private readonly Func<DataAuthoringBatchPreviewResult> _buildPreview;
        private readonly Func<DataAuthoringBatchPreviewResult, DataAuthoringBatchApplyResult> _applyPreview;
        private Vector2 _scroll;

        public DataAuthoringBatchWindowHost(
            DataAuthoringBatchWindowOptions options,
            Func<DataAuthoringBatchPreviewResult> buildPreview,
            Func<DataAuthoringBatchPreviewResult, DataAuthoringBatchApplyResult> applyPreview)
        {
            _options = options ?? DataAuthoringBatchWindowOptions.CreateDefault("Batch Preview", "Apply");
            _buildPreview = buildPreview ?? throw new ArgumentNullException(nameof(buildPreview));
            _applyPreview = applyPreview ?? throw new ArgumentNullException(nameof(applyPreview));
            Preview = EmptyPreview;
            LastApplyResult = EmptyApplyResult;
        }

        public DataAuthoringBatchPreviewResult Preview { get; private set; }
        public DataAuthoringBatchApplyResult LastApplyResult { get; private set; }

        public void RefreshPreview()
        {
            Preview = _buildPreview() ?? EmptyPreview;
        }

        public DataAuthoringBatchApplyResult ApplyPreview()
        {
            if (Preview == null || !Preview.CanApply)
            {
                LastApplyResult = new DataAuthoringBatchApplyResult(
                    Array.Empty<DataAuthoringBatchChange>(),
                    Array.Empty<DataAuthoringBatchChange>(),
                    Preview?.BlockingIssues ?? Array.Empty<DataAuthoringIssue>());
                return LastApplyResult;
            }

            LastApplyResult = _applyPreview(Preview) ?? EmptyApplyResult;
            return LastApplyResult;
        }

        public string CreatePreviewReport()
        {
            return DataAuthoringBatchReportExporter.CreateTsvReport(Preview ?? EmptyPreview, _options.ReportLabels);
        }

        public string CreateApplyReport()
        {
            return DataAuthoringBatchReportExporter.CreateTsvReport(LastApplyResult ?? EmptyApplyResult, _options.ReportLabels);
        }

        public void Draw()
        {
            DrawToolbar();
            DrawSummary();
            DrawTable();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_options.RefreshButtonText, EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    RefreshPreview();
                }

                using (new EditorGUI.DisabledScope((Preview?.Changes.Count ?? 0) == 0 && (Preview?.BlockingIssues.Count ?? 0) == 0))
                {
                    if (GUILayout.Button(_options.CopyPreviewButtonText, EditorStyles.toolbarButton, GUILayout.Width(88f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = CreatePreviewReport();
                        Notify("Batch preview report copied.");
                    }

                    if (GUILayout.Button(_options.ExportPreviewButtonText, EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    {
                        ExportPreviewReport();
                    }
                }

                using (new EditorGUI.DisabledScope(!(LastApplyResult?.HasReportRows ?? false)))
                {
                    if (GUILayout.Button(_options.CopyApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(118f)))
                    {
                        EditorGUIUtility.systemCopyBuffer = CreateApplyReport();
                        Notify("Batch apply report copied.");
                    }

                    if (GUILayout.Button(_options.ExportApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(118f)))
                    {
                        ExportApplyReport();
                    }
                }

                using (new EditorGUI.DisabledScope(!(Preview?.CanApply ?? false)))
                {
                    if (GUILayout.Button(_options.ApplyButtonText, EditorStyles.toolbarButton, GUILayout.Width(128f)))
                    {
                        ApplyPreviewFromGui();
                    }
                }
            }
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(_options.SummaryTitle, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    string.Format(
                        _options.SummaryFormat,
                        Preview?.Changes.Count ?? 0,
                        Preview?.BlockingIssues.Count ?? 0),
                    EditorStyles.miniLabel);

                if ((Preview?.BlockingIssues.Count ?? 0) > 0)
                {
                    EditorGUILayout.HelpBox(_options.BlockingMessage, MessageType.Error);
                    foreach (var issue in Preview.BlockingIssues.Take(5))
                    {
                        EditorGUILayout.LabelField($"{issue.StableId} / {issue.FieldPath} / {issue.Message}", EditorStyles.miniLabel);
                    }
                }

                if (LastApplyResult?.HasReportRows ?? false)
                {
                    EditorGUILayout.LabelField(
                        string.Format(
                            _options.LastApplyFormat,
                            LastApplyResult.AppliedChanges.Count,
                            LastApplyResult.SkippedChanges.Count,
                            LastApplyResult.BlockingIssues.Count),
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawTable()
        {
            DrawHeader();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if ((Preview?.Changes.Count ?? 0) == 0)
            {
                EditorGUILayout.HelpBox(_options.EmptyMessage, MessageType.Info);
            }

            foreach (var change in Preview?.Changes ?? Array.Empty<DataAuthoringBatchChange>())
            {
                DrawRow(change);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_options.GroupHeader, EditorStyles.boldLabel, GUILayout.Width(80f));
                GUILayout.Label(_options.StableIdHeader, EditorStyles.boldLabel, GUILayout.Width(180f));
                GUILayout.Label(_options.FieldHeader, EditorStyles.boldLabel, GUILayout.Width(150f));
                GUILayout.Label(_options.OldValueHeader, EditorStyles.boldLabel, GUILayout.Width(160f));
                GUILayout.Label(_options.NewValueHeader, EditorStyles.boldLabel);
                GUILayout.Label(_options.ActionHeader, EditorStyles.boldLabel, GUILayout.Width(72f));
            }
        }

        private void DrawRow(DataAuthoringBatchChange change)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(change.GroupName, GUILayout.Width(80f));
                EditorGUILayout.LabelField(change.StableId, GUILayout.Width(180f));
                EditorGUILayout.LabelField(change.FieldPath, GUILayout.Width(150f));
                EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(change.OldValue) ? _options.EmptyValueText : change.OldValue, GUILayout.Width(160f));
                EditorGUILayout.LabelField(change.NewValue);
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

        private void ApplyPreviewFromGui()
        {
            if (!(Preview?.CanApply ?? false))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    _options.ApplyDialogTitle,
                    string.Format(_options.ApplyDialogFormat, Preview.Changes.Count),
                    _options.ApplyButtonText,
                    "Cancel"))
            {
                return;
            }

            var result = ApplyPreview();
            RefreshPreview();
            Notify($"Applied {result.AppliedCount} batch changes.");
        }

        private void ExportPreviewReport()
        {
            var path = SaveReportFile(_options.PreviewExportDialogTitle, _options.PreviewExportFilePrefix);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            File.WriteAllText(path, CreatePreviewReport(), System.Text.Encoding.UTF8);
            _options.ReportExported?.Invoke(new DataAuthoringBatchReportExport(
                path,
                _options.PreviewExportActionKind,
                Preview?.BlockingIssues.Count ?? 0,
                Preview?.Changes.Count ?? 0,
                isApplyReport: false));
            Notify("Batch preview report exported.");
        }

        private void ExportApplyReport()
        {
            var path = SaveReportFile(_options.ApplyExportDialogTitle, _options.ApplyExportFilePrefix);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            File.WriteAllText(path, CreateApplyReport(), System.Text.Encoding.UTF8);
            _options.ReportExported?.Invoke(new DataAuthoringBatchReportExport(
                path,
                _options.ApplyExportActionKind,
                LastApplyResult?.BlockingIssues.Count ?? 0,
                (LastApplyResult?.AppliedChanges.Count ?? 0) + (LastApplyResult?.SkippedChanges.Count ?? 0),
                isApplyReport: true));
            Notify("Batch apply report exported.");
        }

        private static string SaveReportFile(string title, string filePrefix)
        {
            return EditorUtility.SaveFilePanel(
                title,
                "Assets",
                $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.tsv",
                "tsv");
        }

        private void Notify(string message)
        {
            _options.Notify?.Invoke(message);
        }
    }
}
