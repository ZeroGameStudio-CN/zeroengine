using System;
using System.IO;
using System.Text;

namespace ZGS.DataToolkit.Editor
{
    public static class TabularCsvExporter
    {
        public static void WriteCsvFolder(TabularWorkbook workbook, string folder)
        {
            WriteDelimitedFolder(workbook, folder, ",", ".csv", EscapeCsvCell);
        }

        public static void WriteTsvFolder(TabularWorkbook workbook, string folder)
        {
            WriteDelimitedFolder(workbook, folder, "\t", ".tsv", EscapeTsvCell);
        }

        private static void WriteDelimitedFolder(
            TabularWorkbook workbook,
            string folder,
            string delimiter,
            string extension,
            Func<string, string> escape)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("Export folder cannot be empty.", nameof(folder));
            }

            Directory.CreateDirectory(folder);
            foreach (var sheet in workbook.Sheets)
            {
                var path = Path.Combine(folder, SanitizeFileName(sheet.Name) + extension);
                var builder = new StringBuilder();
                builder.AppendLine(string.Join(delimiter, MapCells(sheet.Columns, escape)));
                foreach (var row in sheet.Rows)
                {
                    builder.AppendLine(string.Join(delimiter, MapCells(row.Cells, escape)));
                }

                File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }

        private static string[] MapCells(System.Collections.Generic.IReadOnlyList<string> cells, Func<string, string> escape)
        {
            var values = new string[cells.Count];
            for (var i = 0; i < cells.Count; i++)
            {
                values[i] = escape(cells[i] ?? string.Empty);
            }

            return values;
        }

        private static string EscapeCsvCell(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string EscapeTsvCell(string value)
        {
            return value.Replace('\t', ' ').Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
