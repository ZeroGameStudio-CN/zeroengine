using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAuthoringImportService
    {
        public static TabularImportPreview BuildPreview(
            TabularImportWorkbook workbook,
            IEnumerable<IDataAuthoringImportAdapter> adapters,
            bool createMissingAssets)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            var preview = new TabularImportPreview();
            var safeAdapters = (adapters ?? Array.Empty<IDataAuthoringImportAdapter>())
                .Where(adapter => adapter != null)
                .ToArray();

            foreach (var adapter in safeAdapters)
            {
                AddSchemaIssues(preview, workbook, adapter);
                var adapterPreview = adapter.Preview(workbook, createMissingAssets);
                if (adapterPreview == null)
                {
                    continue;
                }

                preview.AddIssues(adapterPreview.Issues);
                preview.AddChanges(adapterPreview.Changes);
            }

            AddUnknownSheetWarnings(preview, workbook, safeAdapters);
            return preview;
        }

        public static void Apply(TabularImportPreview preview, IEnumerable<IDataAuthoringImportAdapter> adapters)
        {
            if (preview == null)
            {
                throw new ArgumentNullException(nameof(preview));
            }

            if (preview.HasBlockingErrors)
            {
                throw new InvalidOperationException("Cannot apply an import preview with blocking errors.");
            }

            var adapterIds = preview.Changes
                .Select(change => change.AdapterId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var adapter in adapters ?? Array.Empty<IDataAuthoringImportAdapter>())
            {
                if (adapter != null && adapterIds.Contains(adapter.AdapterId))
                {
                    adapter.Apply(preview);
                }
            }
        }

        public static void AddImportReportSheets(
            TabularWorkbook workbook,
            TabularImportPreview preview,
            IEnumerable<DataAuthoringIssue> postImportIssues)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            preview ??= new TabularImportPreview();
            var summary = workbook.GetOrCreateSheet("ImportSummary");
            summary.SetColumns("metric", "value");
            summary.AddRow("issues", preview.Issues.Count);
            summary.AddRow("errors", preview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Error));
            summary.AddRow("warnings", preview.Issues.Count(issue => issue.Severity == DataAuthoringIssueSeverity.Warning));
            summary.AddRow("changes", preview.Changes.Count);

            var issues = workbook.GetOrCreateSheet("ImportIssues");
            issues.SetColumns("severity", "sheet", "row", "column", "assetPath", "stableId", "fieldPath", "message");
            foreach (var issue in preview.Issues)
            {
                issues.AddRow(issue.Severity, issue.SheetName, issue.RowNumber, issue.ColumnName, issue.AssetPath, issue.StableId, issue.FieldPath, issue.Message);
            }

            var changes = workbook.GetOrCreateSheet("ImportChanges");
            changes.SetColumns("kind", "sheet", "row", "column", "assetPath", "stableId", "fieldPath", "oldValue", "newValue");
            foreach (var change in preview.Changes)
            {
                changes.AddRow(change.Kind, change.SheetName, change.RowNumber, change.ColumnName, change.AssetPath, change.StableId, change.FieldPath, change.OldValue, change.NewValue);
            }

            DataAuthoringValidationService.AddValidationReportSheet(workbook, postImportIssues ?? Array.Empty<DataAuthoringIssue>());
        }

        private static void AddSchemaIssues(TabularImportPreview preview, TabularImportWorkbook workbook, IDataAuthoringImportAdapter adapter)
        {
            foreach (var sheetName in adapter.RequiredSheets ?? Array.Empty<string>())
            {
                var sheet = workbook.GetSheet(sheetName);
                if (sheet == null)
                {
                    preview.AddIssue(TabularImportIssue.Error(sheetName, 1, string.Empty, string.Empty, string.Empty, string.Empty, "Required sheet is missing."));
                    continue;
                }

                foreach (var requiredColumn in adapter.GetRequiredColumns(sheetName) ?? Array.Empty<string>())
                {
                    if (!sheet.HasColumn(requiredColumn))
                    {
                        preview.AddIssue(TabularImportIssue.Error(sheetName, 1, requiredColumn, string.Empty, string.Empty, requiredColumn, "Required column is missing."));
                    }
                }
                AddUnknownColumnWarnings(preview, sheet, adapter);
            }

            foreach (var sheetName in adapter.OptionalSheets ?? Array.Empty<string>())
            {
                var sheet = workbook.GetSheet(sheetName);
                if (sheet != null)
                {
                    foreach (var requiredColumn in adapter.GetRequiredColumns(sheetName) ?? Array.Empty<string>())
                    {
                        if (!sheet.HasColumn(requiredColumn))
                        {
                            preview.AddIssue(TabularImportIssue.Error(sheetName, 1, requiredColumn, string.Empty, string.Empty, requiredColumn, "Required column is missing."));
                        }
                    }

                    AddUnknownColumnWarnings(preview, sheet, adapter);
                }
            }
        }

        private static void AddUnknownSheetWarnings(
            TabularImportPreview preview,
            TabularImportWorkbook workbook,
            IReadOnlyList<IDataAuthoringImportAdapter> adapters)
        {
            var knownSheets = adapters
                .SelectMany(adapter => (adapter.RequiredSheets ?? Array.Empty<string>()).Concat(adapter.OptionalSheets ?? Array.Empty<string>()))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Name == "ValidationReport" || knownSheets.Contains(sheet.Name))
                {
                    continue;
                }

                preview.AddIssue(TabularImportIssue.Warning(sheet.Name, 1, string.Empty, string.Empty, string.Empty, string.Empty, "Unknown sheet will be ignored."));
            }
        }

        private static void AddUnknownColumnWarnings(TabularImportPreview preview, TabularImportSheet sheet, IDataAuthoringImportAdapter adapter)
        {
            var knownColumns = (adapter.GetKnownColumns(sheet.Name) ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            foreach (var column in sheet.Columns)
            {
                if (knownColumns.Count > 0 && !knownColumns.Contains(column))
                {
                    preview.AddIssue(TabularImportIssue.Warning(sheet.Name, 1, column, string.Empty, string.Empty, column, "Unknown column will be ignored."));
                }
            }
        }
    }
}
