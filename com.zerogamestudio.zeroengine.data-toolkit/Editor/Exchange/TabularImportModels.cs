using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class TabularImportWorkbook
    {
        private readonly Dictionary<string, TabularImportSheet> _sheets = new(StringComparer.Ordinal);

        public IReadOnlyList<TabularImportSheet> Sheets => _sheets.Values
            .OrderBy(sheet => sheet.Name, StringComparer.Ordinal)
            .ToArray();

        public void AddSheet(TabularImportSheet sheet)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            _sheets[sheet.Name] = sheet;
        }

        public TabularImportSheet GetSheet(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return _sheets.TryGetValue(name.Trim(), out var sheet) ? sheet : null;
        }
    }

    public sealed class TabularImportSheet
    {
        public TabularImportSheet(string name, IReadOnlyList<string> columns, IReadOnlyList<TabularImportRow> rows)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Sheet name cannot be empty.", nameof(name))
                : name.Trim();
            Columns = columns?.Select(column => column ?? string.Empty).ToArray() ?? Array.Empty<string>();
            Rows = rows?.ToArray() ?? Array.Empty<TabularImportRow>();
        }

        public string Name { get; }
        public IReadOnlyList<string> Columns { get; }
        public IReadOnlyList<TabularImportRow> Rows { get; }

        public bool HasColumn(string columnName)
        {
            return Columns.Contains(columnName, StringComparer.Ordinal);
        }
    }

    public sealed class TabularImportRow
    {
        private readonly Dictionary<string, string> _cells;

        public TabularImportRow(string sheetName, int rowNumber, IReadOnlyList<string> columns, IReadOnlyList<string> cells)
        {
            SheetName = sheetName ?? string.Empty;
            RowNumber = rowNumber;
            _cells = new Dictionary<string, string>(StringComparer.Ordinal);

            var safeColumns = columns ?? Array.Empty<string>();
            var safeCells = cells ?? Array.Empty<string>();
            for (var i = 0; i < safeColumns.Count; i++)
            {
                _cells[safeColumns[i] ?? string.Empty] = i < safeCells.Count ? safeCells[i] ?? string.Empty : string.Empty;
            }
        }

        public string SheetName { get; }
        public int RowNumber { get; }

        public string GetCell(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return string.Empty;
            }

            return _cells.TryGetValue(columnName, out var value) ? value : string.Empty;
        }

        public bool HasCell(string columnName)
        {
            return !string.IsNullOrWhiteSpace(columnName) && _cells.ContainsKey(columnName);
        }
    }

    public enum TabularImportChangeKind
    {
        CreateAsset,
        UpdateScalar,
        ReplaceList,
        AddListEntry,
        RemoveListEntry,
        UpdateReference,
        Skip
    }

    public readonly struct TabularImportIssue
    {
        public TabularImportIssue(
            DataAuthoringIssueSeverity severity,
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
            Message = string.IsNullOrWhiteSpace(message) ? severity.ToString() : message;
        }

        public DataAuthoringIssueSeverity Severity { get; }
        public string SheetName { get; }
        public int RowNumber { get; }
        public string ColumnName { get; }
        public string AssetPath { get; }
        public string StableId { get; }
        public string FieldPath { get; }
        public string Message { get; }

        public static TabularImportIssue Info(string sheetName, int rowNumber, string columnName, string assetPath, string stableId, string fieldPath, string message)
        {
            return new TabularImportIssue(DataAuthoringIssueSeverity.Info, sheetName, rowNumber, columnName, assetPath, stableId, fieldPath, message);
        }

        public static TabularImportIssue Warning(string sheetName, int rowNumber, string columnName, string assetPath, string stableId, string fieldPath, string message)
        {
            return new TabularImportIssue(DataAuthoringIssueSeverity.Warning, sheetName, rowNumber, columnName, assetPath, stableId, fieldPath, message);
        }

        public static TabularImportIssue Error(string sheetName, int rowNumber, string columnName, string assetPath, string stableId, string fieldPath, string message)
        {
            return new TabularImportIssue(DataAuthoringIssueSeverity.Error, sheetName, rowNumber, columnName, assetPath, stableId, fieldPath, message);
        }
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
        private readonly List<TabularImportIssue> _issues = new();
        private readonly List<TabularImportChange> _changes = new();

        public IReadOnlyList<TabularImportIssue> Issues => _issues;
        public IReadOnlyList<TabularImportChange> Changes => _changes;
        public bool HasBlockingErrors => _issues.Any(issue => issue.Severity == DataAuthoringIssueSeverity.Error);

        public void AddIssue(TabularImportIssue issue)
        {
            _issues.Add(issue);
        }

        public void AddIssues(IEnumerable<TabularImportIssue> issues)
        {
            if (issues == null)
            {
                return;
            }

            _issues.AddRange(issues);
        }

        public void AddChange(TabularImportChange change)
        {
            _changes.Add(change);
        }

        public void AddChanges(IEnumerable<TabularImportChange> changes)
        {
            if (changes == null)
            {
                return;
            }

            _changes.AddRange(changes);
        }
    }

    public interface IDataAuthoringImportAdapter
    {
        string AdapterId { get; }
        IReadOnlyList<string> RequiredSheets { get; }
        IReadOnlyList<string> OptionalSheets { get; }
        IReadOnlyList<string> GetRequiredColumns(string sheetName);
        IReadOnlyList<string> GetKnownColumns(string sheetName);
        TabularImportPreview Preview(TabularImportWorkbook workbook, bool createMissingAssets);
        void Apply(TabularImportPreview preview);
    }
}
