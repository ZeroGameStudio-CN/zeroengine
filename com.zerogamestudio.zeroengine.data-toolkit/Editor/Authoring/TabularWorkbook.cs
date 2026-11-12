using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ZGS.DataToolkit.Editor
{
    public sealed class TabularWorkbook
    {
        private readonly List<TabularSheet> _sheets = new List<TabularSheet>();

        public IReadOnlyList<TabularSheet> Sheets => _sheets;

        public TabularSheet GetOrCreateSheet(string name)
        {
            var sheet = _sheets.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (sheet != null)
            {
                return sheet;
            }

            sheet = new TabularSheet(name);
            _sheets.Add(sheet);
            return sheet;
        }
    }

    public sealed class TabularSheet
    {
        private readonly List<string> _columns = new List<string>();
        private readonly List<TabularRow> _rows = new List<TabularRow>();

        public TabularSheet(string name)
        {
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public IReadOnlyList<string> Columns => _columns;
        public IReadOnlyList<TabularRow> Rows => _rows;

        public void SetColumns(params string[] columns)
        {
            _columns.Clear();
            _columns.AddRange((columns ?? Array.Empty<string>()).Select(column => column ?? string.Empty));
        }

        public void AddRow(params object[] values)
        {
            _rows.Add(new TabularRow((values ?? Array.Empty<object>()).Select(FormatCell).ToArray()));
        }

        private static string FormatCell(object value)
        {
            return value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
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

    public static class TabularCsvExporter
    {
        public static void WriteCsvFolder(TabularWorkbook workbook, string folder)
        {
            WriteDelimitedFolder(workbook, folder, ',', ".csv");
        }

        public static void WriteTsvFolder(TabularWorkbook workbook, string folder)
        {
            WriteDelimitedFolder(workbook, folder, '\t', ".tsv");
        }

        private static void WriteDelimitedFolder(TabularWorkbook workbook, string folder, char delimiter, string extension)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("Output folder is required.", nameof(folder));
            }

            Directory.CreateDirectory(folder);
            foreach (var sheet in workbook?.Sheets ?? Array.Empty<TabularSheet>())
            {
                var path = Path.Combine(folder, SanitizeFileName(sheet.Name) + extension);
                File.WriteAllText(path, BuildDelimitedText(sheet, delimiter), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }

        private static string BuildDelimitedText(TabularSheet sheet, char delimiter)
        {
            var builder = new StringBuilder();
            AppendRow(builder, sheet.Columns, delimiter);
            foreach (var row in sheet.Rows)
            {
                AppendRow(builder, row.Cells, delimiter);
            }

            return builder.ToString();
        }

        private static void AppendRow(StringBuilder builder, IReadOnlyList<string> cells, char delimiter)
        {
            for (var i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(delimiter);
                }

                builder.Append(Escape(cells[i], delimiter));
            }

            builder.AppendLine();
        }

        private static string Escape(string value, char delimiter)
        {
            value ??= string.Empty;
            var mustQuote = value.IndexOfAny(new[] { delimiter, '"', '\r', '\n' }) >= 0;
            if (!mustQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (string.IsNullOrWhiteSpace(value) ? "Sheet" : value)
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray();
            return new string(chars);
        }
    }
}
