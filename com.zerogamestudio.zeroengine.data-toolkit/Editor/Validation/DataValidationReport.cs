using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataValidationReport
    {
        public static readonly DataValidationReport Empty = new(Array.Empty<DataValidationIssue>());

        public DataValidationReport(IEnumerable<DataValidationIssue> issues)
        {
            Issues = (issues ?? Array.Empty<DataValidationIssue>())
                .Where(issue => issue != null)
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.TypeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.AssetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.FieldPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<DataValidationIssue> Issues { get; }
        public int ErrorCount => Issues.Count(issue => issue.Severity == DataValidationSeverity.Error);
        public int WarningCount => Issues.Count(issue => issue.Severity == DataValidationSeverity.Warning);
        public int InfoCount => Issues.Count(issue => issue.Severity == DataValidationSeverity.Info);
        public bool HasIssues => Issues.Count > 0;

        public IReadOnlyList<DataValidationIssue> GetIssuesForAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return Array.Empty<DataValidationIssue>();
            }

            return Issues.Where(issue => issue.BelongsToPath(assetPath)).ToArray();
        }

        public void GetIssueCountsForAssetPath(string assetPath, out int errors, out int warnings)
        {
            errors = 0;
            warnings = 0;

            foreach (var issue in GetIssuesForAssetPath(assetPath))
            {
                if (issue.Severity == DataValidationSeverity.Error)
                {
                    errors++;
                }
                else if (issue.Severity == DataValidationSeverity.Warning)
                {
                    warnings++;
                }
            }
        }
    }
}
