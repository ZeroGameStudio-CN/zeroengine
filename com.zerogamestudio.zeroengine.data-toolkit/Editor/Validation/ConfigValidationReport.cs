using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class ConfigValidationReport
    {
        private readonly List<ConfigValidationIssue> issues = new();

        public ConfigValidationReport(string title)
        {
            Title = title ?? string.Empty;
        }

        public string Title { get; }
        public IReadOnlyList<ConfigValidationIssue> Issues => issues;
        public int ErrorCount => issues.Count(issue => issue.Severity == ConfigValidationSeverity.Error);
        public int WarningCount => issues.Count(issue => issue.Severity == ConfigValidationSeverity.Warning);
        public bool HasErrors => ErrorCount > 0;

        public void Add(
            ConfigValidationSeverity severity,
            string code,
            string message,
            string assetPath = "",
            string objectName = "")
        {
            issues.Add(new ConfigValidationIssue(severity, code, message, assetPath, objectName));
        }

        public IEnumerable<ConfigValidationIssue> GetDeterministicIssues()
        {
            return issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.AssetPath, StringComparer.Ordinal)
                .ThenBy(issue => issue.ObjectName, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal);
        }
    }
}
