using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public static class DataAssetCsvImporter
    {
        public static DataAssetChangePreview PreviewFieldUpdatesFromCsv(string csv)
        {
            return DataAssetFieldChangeService.PreviewChanges(ParseFieldUpdates(csv), null);
        }

        public static DataAssetChangePreview PreviewFieldUpdatesFromCsv(
            string csv,
            IReadOnlyDictionary<string, Object> assetsByPath)
        {
            return DataAssetFieldChangeService.PreviewChanges(ParseFieldUpdates(csv), assetsByPath);
        }

        public static IReadOnlyList<DataAssetFieldUpdate> ParseFieldUpdates(string csv)
        {
            var lines = SplitLines(csv).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (lines.Length == 0)
            {
                return Array.Empty<DataAssetFieldUpdate>();
            }

            var headers = ParseRow(lines[0]);
            var assetPathIndex = FindHeader(headers, "Path", "AssetPath");
            var fieldPathIndex = FindHeader(headers, "Field", "FieldPath");
            var valueIndex = FindHeader(headers, "Value", "NewValue");
            if (assetPathIndex < 0 || fieldPathIndex < 0 || valueIndex < 0)
            {
                throw new ArgumentException("CSV must include Path, Field, and Value columns.");
            }

            var updates = new List<DataAssetFieldUpdate>();
            for (var i = 1; i < lines.Length; i++)
            {
                var row = ParseRow(lines[i]);
                updates.Add(new DataAssetFieldUpdate(
                    GetCell(row, assetPathIndex),
                    GetCell(row, fieldPathIndex),
                    GetCell(row, valueIndex)));
            }

            return updates;
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }

        private static int FindHeader(IReadOnlyList<string> headers, params string[] names)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                foreach (var name in names)
                {
                    if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string GetCell(IReadOnlyList<string> row, int index)
        {
            return index >= 0 && index < row.Count ? row[index] : string.Empty;
        }

        private static IReadOnlyList<string> ParseRow(string line)
        {
            var cells = new List<string>();
            var value = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < (line ?? string.Empty).Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    cells.Add(value.ToString());
                    value.Length = 0;
                }
                else
                {
                    value.Append(c);
                }
            }

            cells.Add(value.ToString());
            return cells;
        }
    }
}
