using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAuthoringValidationService
    {
        public static IReadOnlyList<DataAuthoringIssue> ValidateProfile(DataAuthoringProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return profile.Adapters
                .SelectMany(ValidateAdapter)
                .ToArray();
        }

        public static IReadOnlyList<DataAuthoringIssue> ValidateAdapter(IDataAuthoringAssetAdapter adapter)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            var issues = new List<DataAuthoringIssue>();
            foreach (var record in adapter.GetAssets() ?? Array.Empty<DataAuthoringAssetRecord>())
            {
                if (record?.Asset == null)
                {
                    continue;
                }

                var assetIssues = adapter.Validate(record.Asset);
                if (assetIssues != null)
                {
                    issues.AddRange(assetIssues);
                }
            }

            return issues;
        }

        public static void AddValidationReportSheet(TabularWorkbook workbook, IEnumerable<DataAuthoringIssue> issues)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            var sheet = workbook.GetOrCreateSheet("ValidationReport");
            sheet.SetColumns("severity", "assetPath", "assetType", "stableId", "fieldPath", "message");
            foreach (var issue in issues ?? Array.Empty<DataAuthoringIssue>())
            {
                sheet.AddRow(
                    issue.Severity,
                    issue.AssetPath,
                    issue.AssetType,
                    issue.StableId,
                    issue.FieldPath,
                    issue.Message);
            }
        }
    }
}
