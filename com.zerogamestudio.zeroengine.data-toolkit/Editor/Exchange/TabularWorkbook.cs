using System;
using System.Collections.Generic;
using System.Linq;

namespace ZGS.DataToolkit.Editor
{
    public sealed class TabularWorkbook
    {
        private readonly Dictionary<string, TabularSheet> _sheets = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<TabularSheet> Sheets => _sheets.Values
            .OrderBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        public TabularSheet GetOrCreateSheet(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Sheet name cannot be empty.", nameof(name));
            }

            var normalized = name.Trim();
            if (!_sheets.TryGetValue(normalized, out var sheet))
            {
                sheet = new TabularSheet(normalized);
                _sheets.Add(normalized, sheet);
            }

            return sheet;
        }
    }

    public sealed class TabularSheet
    {
        private readonly List<string> _columns = new();
        private readonly List<TabularRow> _rows = new();

        public TabularSheet(string name)
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? throw new ArgumentException("Sheet name cannot be empty.", nameof(name))
                : name.Trim();
        }

        public string Name { get; }
        public IReadOnlyList<string> Columns => _columns;
        public IReadOnlyList<TabularRow> Rows => _rows;

        public void SetColumns(params string[] columns)
        {
            _columns.Clear();
            foreach (var column in columns ?? Array.Empty<string>())
            {
                _columns.Add(column ?? string.Empty);
            }
        }

        public void AddRow(params object[] cells)
        {
            _rows.Add(new TabularRow((cells ?? Array.Empty<object>())
                .Select(cell => cell?.ToString() ?? string.Empty)
                .ToArray()));
        }
    }

    public sealed class TabularRow
    {
        public TabularRow(IReadOnlyList<string> cells)
        {
            Cells = cells ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> Cells { get; }
    }
}
