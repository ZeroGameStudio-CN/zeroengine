using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaPreviewReportExporter
    {
        public static string ToJson(FormulaPreviewBatchReport report)
        {
            var payload = new FormulaPreviewBatchReportJson
            {
                profileId = report?.Profile?.ProfileId ?? string.Empty,
                caseCount = report?.Results.Count ?? 0,
                results = new List<FormulaPreviewCaseResultJson>(),
            };

            if (report != null)
            {
                foreach (var result in report.Results)
                {
                    payload.results.Add(new FormulaPreviewCaseResultJson
                    {
                        caseId = result.Case?.Id ?? string.Empty,
                        caseName = result.Case?.DisplayName ?? string.Empty,
                        succeeded = result.Succeeded,
                        result = result.Value,
                        diagnosticCount = result.Report?.Diagnostics.Count ?? 0,
                        stepCount = result.Report?.Steps.Count ?? 0,
                    });
                }
            }

            return JsonUtility.ToJson(payload, false);
        }

        public static string ToMarkdown(FormulaPreviewBatchReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Formula Preview Report");
            builder.AppendLine();
            builder.AppendLine($"Profile: {report?.Profile?.DisplayName ?? string.Empty}");
            builder.AppendLine($"Cases: {report?.Results.Count ?? 0}");

            if (report == null || report.Results.Count == 0)
                return builder.ToString();

            builder.AppendLine();
            builder.AppendLine("## Results");
            foreach (var result in report.Results)
            {
                builder.AppendLine(
                    $"- {result.Case?.Id ?? string.Empty} ({result.Case?.DisplayName ?? string.Empty}): Result {result.Value}, Succeeded {result.Succeeded}, Diagnostics: {result.Report?.Diagnostics.Count ?? 0}, Steps: {result.Report?.Steps.Count ?? 0}");
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class FormulaPreviewBatchReportJson
        {
            public string profileId;
            public int caseCount;
            public List<FormulaPreviewCaseResultJson> results;
        }

        [Serializable]
        private sealed class FormulaPreviewCaseResultJson
        {
            public string caseId;
            public string caseName;
            public bool succeeded;
            public float result;
            public int diagnosticCount;
            public int stepCount;
        }
    }
}
