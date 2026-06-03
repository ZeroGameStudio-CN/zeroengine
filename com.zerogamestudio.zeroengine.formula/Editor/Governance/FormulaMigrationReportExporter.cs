using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaMigrationReportExporter
    {
        public static string ToJson(FormulaMigrationReport report)
        {
            var payload = new FormulaMigrationReportJson
            {
                kind = report?.Kind.ToString() ?? string.Empty,
                applied = report?.Applied ?? false,
                changeCount = report?.Changes.Count ?? 0,
                changes = new List<FormulaMigrationChangeJson>(),
            };

            if (report != null)
            {
                foreach (var change in report.Changes)
                {
                    payload.changes.Add(new FormulaMigrationChangeJson
                    {
                        stepIndex = change.StepIndex,
                        oldValue = change.OldValue,
                        newValue = change.NewValue,
                        message = change.Message,
                    });
                }
            }

            return JsonUtility.ToJson(payload, false);
        }

        public static string ToMarkdown(FormulaMigrationReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Formula Migration Report");
            builder.AppendLine();
            builder.AppendLine($"Kind: {report?.Kind.ToString() ?? string.Empty}");
            builder.AppendLine($"Applied: {report?.Applied ?? false}");
            builder.AppendLine($"Changes: {report?.Changes.Count ?? 0}");

            if (report == null || report.Changes.Count == 0)
                return builder.ToString();

            builder.AppendLine();
            builder.AppendLine("## Changes");
            foreach (var change in report.Changes)
            {
                builder.AppendLine(
                    $"- Step {change.StepIndex}: `{change.OldValue}` -> `{change.NewValue}` - {change.Message}");
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class FormulaMigrationReportJson
        {
            public string kind;
            public bool applied;
            public int changeCount;
            public List<FormulaMigrationChangeJson> changes;
        }

        [Serializable]
        private sealed class FormulaMigrationChangeJson
        {
            public int stepIndex;
            public string oldValue;
            public string newValue;
            public string message;
        }
    }
}
