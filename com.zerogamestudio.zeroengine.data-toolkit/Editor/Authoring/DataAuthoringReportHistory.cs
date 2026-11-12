using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;

namespace ZGS.DataToolkit.Editor
{
    public readonly struct DataAuthoringReportHistoryEntry
    {
        public DataAuthoringReportHistoryEntry(
            string profileId,
            string actionKind,
            int issueCount,
            int changeCount,
            string outputFolder,
            string path,
            DateTime exportedAt)
        {
            ProfileId = profileId ?? string.Empty;
            ActionKind = actionKind ?? string.Empty;
            IssueCount = Math.Max(0, issueCount);
            ChangeCount = Math.Max(0, changeCount);
            OutputFolder = outputFolder ?? string.Empty;
            Path = path ?? string.Empty;
            ExportedAt = exportedAt;
        }

        public string ProfileId { get; }
        public string ActionKind { get; }
        public int IssueCount { get; }
        public int ChangeCount { get; }
        public string OutputFolder { get; }
        public string Path { get; }
        public DateTime ExportedAt { get; }
    }

    public static class DataAuthoringReportHistory
    {
        public static DataAuthoringReportHistoryEntry CreateEntry(
            string path,
            string profileId,
            string actionKind,
            int issueCount,
            int changeCount,
            DateTime exportedAt)
        {
            return new DataAuthoringReportHistoryEntry(
                profileId,
                actionKind,
                issueCount,
                changeCount,
                GetOutputFolder(path),
                path,
                exportedAt);
        }

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> GetEntries(
            string editorPrefsKey,
            string profileId,
            string actionKind,
            int maxEntryCount)
        {
            return Deserialize(EditorPrefs.GetString(editorPrefsKey, string.Empty), profileId, actionKind, maxEntryCount);
        }

        public static void RecordExport(
            string editorPrefsKey,
            string path,
            string profileId,
            string actionKind,
            int issueCount,
            int changeCount,
            int maxEntryCount)
        {
            var existing = Deserialize(EditorPrefs.GetString(editorPrefsKey, string.Empty), profileId, actionKind, maxEntryCount);
            var entry = CreateEntry(path, profileId, actionKind, issueCount, changeCount, DateTime.Now);
            var updated = CreateUpdatedEntries(existing, entry, maxEntryCount);
            EditorPrefs.SetString(editorPrefsKey, Serialize(updated));
        }

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> CreateUpdatedEntries(
            IEnumerable<DataAuthoringReportHistoryEntry> existingEntries,
            DataAuthoringReportHistoryEntry newEntry,
            int maxEntryCount)
        {
            var entries = new List<DataAuthoringReportHistoryEntry> { newEntry };
            foreach (var entry in existingEntries ?? Array.Empty<DataAuthoringReportHistoryEntry>())
            {
                if (string.Equals(entry.Path, newEntry.Path, StringComparison.Ordinal))
                {
                    continue;
                }

                entries.Add(entry);
            }

            return entries
                .OrderByDescending(entry => entry.ExportedAt)
                .Take(Math.Max(1, maxEntryCount))
                .ToArray();
        }

        public static string Serialize(IEnumerable<DataAuthoringReportHistoryEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries ?? Array.Empty<DataAuthoringReportHistoryEntry>())
            {
                builder.Append(entry.ExportedAt.ToString("O"));
                builder.Append('\t');
                builder.Append(entry.IssueCount);
                builder.Append('\t');
                builder.Append(entry.ChangeCount);
                builder.Append('\t');
                builder.Append(Encode(entry.Path));
                builder.Append('\t');
                builder.Append(Encode(entry.ProfileId));
                builder.Append('\t');
                builder.Append(Encode(entry.ActionKind));
                builder.Append('\t');
                builder.Append(Encode(entry.OutputFolder));
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> Deserialize(
            string value,
            string legacyProfileId,
            string legacyActionKind,
            int maxEntryCount)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<DataAuthoringReportHistoryEntry>();
            }

            var entries = new List<DataAuthoringReportHistoryEntry>();
            var lines = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var columns = line.Split('\t');
                if (columns.Length == 3 && TryParseDate(columns[0], out var legacyTime) && int.TryParse(columns[1], out var legacyIssues))
                {
                    var legacyPath = Decode(columns[2]);
                    entries.Add(new DataAuthoringReportHistoryEntry(
                        legacyProfileId,
                        legacyActionKind,
                        legacyIssues,
                        0,
                        GetOutputFolder(legacyPath),
                        legacyPath,
                        legacyTime));
                    continue;
                }

                if (columns.Length < 7
                    || !TryParseDate(columns[0], out var exportedAt)
                    || !int.TryParse(columns[1], out var issueCount)
                    || !int.TryParse(columns[2], out var changeCount))
                {
                    continue;
                }

                var path = Decode(columns[3]);
                var profileId = Decode(columns[4]);
                var actionKind = Decode(columns[5]);
                var outputFolder = Decode(columns[6]);
                entries.Add(new DataAuthoringReportHistoryEntry(
                    profileId,
                    actionKind,
                    issueCount,
                    changeCount,
                    string.IsNullOrWhiteSpace(outputFolder) ? GetOutputFolder(path) : outputFolder,
                    path,
                    exportedAt));
            }

            return entries
                .OrderByDescending(entry => entry.ExportedAt)
                .Take(Math.Max(1, maxEntryCount))
                .ToArray();
        }

        private static bool TryParseDate(string value, out DateTime dateTime)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out dateTime);
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string GetOutputFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Replace('\\', '/');
            var index = normalized.LastIndexOf('/');
            return index <= 0 ? string.Empty : normalized.Substring(0, index);
        }
    }
}
