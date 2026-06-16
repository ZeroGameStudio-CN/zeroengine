using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public class DataAuthoringBatchChange
    {
        public DataAuthoringBatchChange(
            Object asset,
            string operationId,
            string groupName,
            string assetPath,
            string stableId,
            string fieldPath,
            string oldValue,
            string newValue)
        {
            Asset = asset;
            OperationId = operationId ?? string.Empty;
            GroupName = groupName ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        public Object Asset { get; }
        public string OperationId { get; }
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
        public int AppliedCount => AppliedChanges.Count;
        public bool HasReportRows => AppliedChanges.Count > 0 || SkippedChanges.Count > 0 || BlockingIssues.Count > 0;
    }

    public readonly struct DataAuthoringBatchReportLabels
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

        public static DataAuthoringBatchReportLabels Default => new(
            "Preview",
            "Blocked",
            "Applied",
            "Skipped",
            "Current asset state no longer matches preview.");
    }
}
