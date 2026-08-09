using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaAssetScanReportExporter
    {
        public static string ToJson(FormulaAssetScanReport report)
        {
            var payload = new FormulaAssetScanReportJson
            {
                assetCount = report?.AssetCount ?? 0,
                errorCount = report?.ErrorCount ?? 0,
                warningCount = report?.WarningCount ?? 0,
                issues = new List<FormulaAssetScanIssueJson>(),
            };

            if (report != null)
            {
                foreach (var issue in report.Issues)
                {
                    payload.issues.Add(new FormulaAssetScanIssueJson
                    {
                        severity = issue.Severity.ToString(),
                        assetPath = issue.AssetPath,
                        message = issue.Message,
                    });
                }
            }

            return JsonUtility.ToJson(payload, false);
        }

        public static string ToMarkdown(FormulaAssetScanReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Formula Scan Report");
            builder.AppendLine();
            builder.AppendLine($"Assets: {report?.AssetCount ?? 0}");
            builder.AppendLine($"Errors: {report?.ErrorCount ?? 0}");
            builder.AppendLine($"Warnings: {report?.WarningCount ?? 0}");

            if (report == null || report.Issues.Count == 0)
                return builder.ToString();

            builder.AppendLine();
            builder.AppendLine("## Issues");
            foreach (var issue in report.Issues)
            {
                builder.AppendLine($"- [{issue.Severity}] `{issue.AssetPath}` - {issue.Message}");
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class FormulaAssetScanReportJson
        {
            public int assetCount;
            public int errorCount;
            public int warningCount;
            public List<FormulaAssetScanIssueJson> issues;
        }

        [Serializable]
        private sealed class FormulaAssetScanIssueJson
        {
            public string severity;
            public string assetPath;
            public string message;
        }
    }
}
