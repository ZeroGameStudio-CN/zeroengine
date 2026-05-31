using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZGS.DataToolkit.Editor
{
    public static class TabularFolderImporter
    {
        public static TabularImportWorkbook ReadFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("Import folder cannot be empty.", nameof(folder));
            }

            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException(folder);
            }

            var csvFiles = Directory.GetFiles(folder, "*.csv").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var tsvFiles = Directory.GetFiles(folder, "*.tsv").OrderBy(path => path, StringComparer.Ordinal).ToArray();

            if (csvFiles.Length > 0 && tsvFiles.Length > 0)
            {
                throw new InvalidOperationException("Cannot import mixed CSV and TSV files from the same folder.");
            }

            var delimiter = csvFiles.Length > 0 ? ',' : '\t';
            var files = csvFiles.Length > 0 ? csvFiles : tsvFiles;
            var workbook = new TabularImportWorkbook();

            foreach (var file in files)
            {
                var sheetName = Path.GetFileNameWithoutExtension(file);
                var records = ParseDelimited(File.ReadAllText(file, Encoding.UTF8), delimiter);
                if (records.Count == 0)
                {
                    workbook.AddSheet(new TabularImportSheet(sheetName, Array.Empty<string>(), Array.Empty<TabularImportRow>()));
                    continue;
                }

                var columns = records[0];
                var rows = records
                    .Skip(1)
                    .Where(row => row.Any(cell => !string.IsNullOrEmpty(cell)))
                    .Select((row, index) => new TabularImportRow(sheetName, index + 2, columns, row))
                    .ToArray();

                workbook.AddSheet(new TabularImportSheet(sheetName, columns, rows));
            }

            return workbook;
        }

        private static List<string[]> ParseDelimited(string text, char delimiter)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];

                if (current == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && current == delimiter)
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    continue;
                }

                if (!inQuotes && (current == '\n' || current == '\r'))
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();

                    if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    continue;
                }

                cell.Append(current);
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }
    }
}
