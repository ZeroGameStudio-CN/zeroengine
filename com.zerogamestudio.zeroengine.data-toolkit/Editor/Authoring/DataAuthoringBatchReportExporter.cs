using System;
using System.Text;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAuthoringBatchReportExporter
    {
        private const string Header = "rowType\tgroup\tassetPath\tstableId\tfieldPath\toldValue\tnewValue\tstatus\tmessage";

        public static string CreateTsvReport(DataAuthoringBatchPreviewResult preview)
        {
            return CreateTsvReport(preview, DataAuthoringBatchReportLabels.Default);
        }

        public static string CreateTsvReport(
            DataAuthoringBatchPreviewResult preview,
            DataAuthoringBatchReportLabels labels)
        {
            labels = ResolveLabels(labels);
            var builder = CreateBuilder();
            if (preview == null)
            {
                return builder.ToString();
            }

            foreach (var change in preview.Changes ?? Array.Empty<DataAuthoringBatchChange>())
            {
                AppendChangeRow(builder, "change", change, labels.PreviewStatus, string.Empty);
            }

            foreach (var issue in preview.BlockingIssues ?? Array.Empty<DataAuthoringIssue>())
            {
                AppendIssueRow(builder, issue, labels.BlockingStatus);
            }

            return builder.ToString();
        }

        public static string CreateTsvReport(DataAuthoringBatchApplyResult result)
        {
            return CreateTsvReport(result, DataAuthoringBatchReportLabels.Default);
        }

        public static string CreateTsvReport(
            DataAuthoringBatchApplyResult result,
            DataAuthoringBatchReportLabels labels)
        {
            labels = ResolveLabels(labels);
            var builder = CreateBuilder();
            if (result == null)
            {
                return builder.ToString();
            }

            foreach (var change in result.AppliedChanges ?? Array.Empty<DataAuthoringBatchChange>())
            {
                AppendChangeRow(builder, "appliedChange", change, labels.AppliedStatus, string.Empty);
            }

            foreach (var change in result.SkippedChanges ?? Array.Empty<DataAuthoringBatchChange>())
            {
                AppendChangeRow(builder, "skippedChange", change, labels.SkippedStatus, labels.SkippedMessage);
            }

            foreach (var issue in result.BlockingIssues ?? Array.Empty<DataAuthoringIssue>())
            {
                AppendIssueRow(builder, issue, labels.BlockingStatus);
            }

            return builder.ToString();
        }

        private static StringBuilder CreateBuilder()
        {
            var builder = new StringBuilder();
            builder.AppendLine(Header);
            return builder;
        }

        private static void AppendChangeRow(
            StringBuilder builder,
            string rowType,
            DataAuthoringBatchChange change,
            string status,
            string message)
        {
            builder
                .Append(rowType).Append('\t')
                .Append(SanitizeTsv(change.GroupName)).Append('\t')
                .Append(SanitizeTsv(change.AssetPath)).Append('\t')
                .Append(SanitizeTsv(change.StableId)).Append('\t')
                .Append(SanitizeTsv(change.FieldPath)).Append('\t')
                .Append(SanitizeTsv(change.OldValue)).Append('\t')
                .Append(SanitizeTsv(change.NewValue)).Append('\t')
                .Append(SanitizeTsv(status)).Append('\t')
                .Append(SanitizeTsv(message))
                .AppendLine();
        }

        private static void AppendIssueRow(StringBuilder builder, DataAuthoringIssue issue, string status)
        {
            builder
                .Append("blockingIssue").Append('\t')
                .Append(SanitizeTsv(issue.AssetType)).Append('\t')
                .Append(SanitizeTsv(issue.AssetPath)).Append('\t')
                .Append(SanitizeTsv(issue.StableId)).Append('\t')
                .Append(SanitizeTsv(issue.FieldPath)).Append('\t')
                .Append('\t')
                .Append('\t')
                .Append(SanitizeTsv(status)).Append('\t')
                .Append(SanitizeTsv(issue.Message))
                .AppendLine();
        }

        private static DataAuthoringBatchReportLabels ResolveLabels(DataAuthoringBatchReportLabels labels)
        {
            return string.IsNullOrEmpty(labels.PreviewStatus)
                ? DataAuthoringBatchReportLabels.Default
                : labels;
        }

        private static string SanitizeTsv(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
