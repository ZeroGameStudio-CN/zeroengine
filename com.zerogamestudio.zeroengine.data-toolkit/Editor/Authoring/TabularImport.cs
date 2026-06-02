using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class TabularImportWorkbook
    {
        private readonly List<TabularImportSheet> _sheets = new List<TabularImportSheet>();

        public IReadOnlyList<TabularImportSheet> Sheets => _sheets;

        public void AddSheet(TabularImportSheet sheet)
        {
            if (sheet == null)
            {
                return;
            }

            _sheets.RemoveAll(candidate => string.Equals(candidate.Name, sheet.Name, StringComparison.Ordinal));
            _sheets.Add(sheet);
        }

        public TabularImportSheet GetSheet(string name)
        {
            return _sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
        }
    }

    public sealed class TabularImportSheet
    {
        public TabularImportSheet(string name, IReadOnlyList<string> columns, IReadOnlyList<TabularImportRow> rows)
        {
            Name = name ?? string.Empty;
            Columns = columns ?? Array.Empty<string>();
            Rows = rows ?? Array.Empty<TabularImportRow>();
        }

        public string Name { get; }
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<TabularImportRow> Rows { get; }
    }

    public sealed class TabularImportRow
    {
        private readonly Dictionary<string, string> _cells;

        public TabularImportRow(string sheetName, int rowNumber, IReadOnlyList<string> columns, IReadOnlyList<string> cells)
        {
            SheetName = sheetName ?? string.Empty;
            RowNumber = rowNumber;
            Columns = columns ?? Array.Empty<string>();
            Cells = cells ?? Array.Empty<string>();
            _cells = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < Columns.Count; i++)
            {
                _cells[Columns[i] ?? string.Empty] = i < Cells.Count ? Cells[i] ?? string.Empty : string.Empty;
            }
        }

        public string SheetName { get; }
        public int RowNumber { get; }
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<string> Cells { get; }

        public bool HasCell(string columnName)
        {
            return !string.IsNullOrWhiteSpace(columnName) && _cells.ContainsKey(columnName);
        }

        public string GetCell(string columnName)
        {
            return !string.IsNullOrWhiteSpace(columnName) && _cells.TryGetValue(columnName, out var value)
                ? value ?? string.Empty
                : string.Empty;
        }
    }

    public enum TabularImportIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct TabularImportIssue
    {
        public TabularImportIssue(
            TabularImportIssueSeverity severity,
            string sheetName,
            int rowNumber,
            string columnName,
            string assetPath,
            string stableId,
            string fieldPath,
            string message)
        {
            Severity = severity;
            SheetName = sheetName ?? string.Empty;
            RowNumber = rowNumber;
            ColumnName = columnName ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public TabularImportIssueSeverity Severity { get; }
        public string SheetName { get; }
        public int RowNumber { get; }
        public string ColumnName { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string Message { get; }
        public bool IsBlocking => Severity == TabularImportIssueSeverity.Error;

        public static TabularImportIssue Error(string sheetName, int rowNumber, string columnName, string assetPath, string stableId, string fieldPath, string message)
        {
            return new TabularImportIssue(TabularImportIssueSeverity.Error, sheetName, rowNumber, columnName, assetPath, stableId, fieldPath, message);
        }

        public static TabularImportIssue Warning(string sheetName, int rowNumber, string columnName, string assetPath, string stableId, string fieldPath, string message)
        {
            return new TabularImportIssue(TabularImportIssueSeverity.Warning, sheetName, rowNumber, columnName, assetPath, stableId, fieldPath, message);
        }
    }

    public enum TabularImportChangeKind
    {
        CreateAsset,
        UpdateScalar,
        ReplaceList
    }

    public readonly struct TabularImportChange
    {
        public TabularImportChange(
            string adapterId,
            TabularImportChangeKind kind,
            string sheetName,
            int rowNumber,
            string columnName,
            string assetPath,
            string stableId,
            string fieldPath,
            string oldValue,
            string newValue)
        {
            AdapterId = adapterId ?? string.Empty;
            Kind = kind;
            SheetName = sheetName ?? string.Empty;
            RowNumber = rowNumber;
            ColumnName = columnName ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            StableId = stableId ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            OldValue = oldValue ?? string.Empty;
            NewValue = newValue ?? string.Empty;
        }

        public string AdapterId { get; }
        public TabularImportChangeKind Kind { get; }
        public string SheetName { get; }
        public int RowNumber { get; }
        public string ColumnName { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string OldValue { get; }
        public string NewValue { get; }
    }

    public sealed class TabularImportPreview
    {
        private readonly List<TabularImportChange> _changes = new List<TabularImportChange>();
        private readonly List<TabularImportIssue> _issues = new List<TabularImportIssue>();

        public IReadOnlyList<TabularImportChange> Changes => _changes;
        public IReadOnlyList<TabularImportIssue> Issues => _issues;
        public IReadOnlyList<TabularImportIssue> BlockingIssues => _issues.Where(issue => issue.IsBlocking).ToArray();
        public bool HasBlockingErrors => _issues.Any(issue => issue.IsBlocking);

        public void AddChange(TabularImportChange change)
        {
            _changes.Add(change);
        }

        public void AddIssue(TabularImportIssue issue)
        {
            _issues.Add(issue);
        }

        public void Merge(TabularImportPreview preview)
        {
            if (preview == null)
            {
                return;
            }

            _changes.AddRange(preview.Changes);
            _issues.AddRange(preview.Issues);
        }
    }

    public static class DataAuthoringImportService
    {
        public static TabularImportPreview BuildPreview(
            TabularImportWorkbook workbook,
            IEnumerable<IDataAuthoringImportAdapter> adapters,
            bool createMissingAssets)
        {
            var combined = new TabularImportPreview();
            foreach (var adapter in adapters ?? Array.Empty<IDataAuthoringImportAdapter>())
            {
                if (adapter == null)
                {
                    continue;
                }

                combined.Merge(adapter.Preview(workbook, createMissingAssets));
            }

            return combined;
        }

        public static void Apply(TabularImportPreview preview, IEnumerable<IDataAuthoringImportAdapter> adapters)
        {
            if (preview != null && preview.HasBlockingErrors)
            {
                throw new InvalidOperationException("Import preview has blocking errors.");
            }

            foreach (var adapter in adapters ?? Array.Empty<IDataAuthoringImportAdapter>())
            {
                adapter?.Apply(preview);
            }
        }
    }
}
