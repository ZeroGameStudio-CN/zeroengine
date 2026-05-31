using System;
using System.Collections.Generic;
using System.Globalization;
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
            ProfileId = string.IsNullOrWhiteSpace(profileId) ? string.Empty : profileId.Trim();
            ActionKind = string.IsNullOrWhiteSpace(actionKind) ? string.Empty : actionKind.Trim();
            Path = path ?? string.Empty;
            IssueCount = Math.Max(0, issueCount);
            ChangeCount = Math.Max(0, changeCount);
            OutputFolder = string.IsNullOrWhiteSpace(outputFolder)
                ? DataAuthoringReportHistory.GetOutputFolder(path)
                : DataAuthoringReportHistory.NormalizeFolder(outputFolder);
            ExportedAt = exportedAt;
        }

        public string ProfileId { get; }
        public string ActionKind { get; }
        public string Path { get; }
        public int IssueCount { get; }
        public int ChangeCount { get; }
        public string OutputFolder { get; }
        public DateTime ExportedAt { get; }
    }

    public static class DataAuthoringReportHistory
    {
        private const string CurrentFormatVersion = "v2";

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> GetEntries(
            string editorPrefsKey,
            string defaultProfileId,
            string defaultActionKind,
            int maxEntryCount)
        {
            if (string.IsNullOrWhiteSpace(editorPrefsKey))
            {
                return Array.Empty<DataAuthoringReportHistoryEntry>();
            }

            return Deserialize(
                EditorPrefs.GetString(editorPrefsKey, string.Empty),
                defaultProfileId,
                defaultActionKind,
                maxEntryCount);
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
            if (string.IsNullOrWhiteSpace(editorPrefsKey) || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var entry = CreateEntry(path, profileId, actionKind, issueCount, changeCount, DateTime.Now);
            var entries = CreateUpdatedEntries(
                GetEntries(editorPrefsKey, profileId, actionKind, maxEntryCount),
                entry,
                maxEntryCount);
            EditorPrefs.SetString(editorPrefsKey, Serialize(entries));
        }

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

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> CreateUpdatedEntries(
            IEnumerable<DataAuthoringReportHistoryEntry> existingEntries,
            DataAuthoringReportHistoryEntry newEntry,
            int maxEntryCount)
        {
            if (string.IsNullOrWhiteSpace(newEntry.Path))
            {
                return NormalizeEntries(existingEntries, Math.Max(0, maxEntryCount));
            }

            var entries = new List<DataAuthoringReportHistoryEntry> { newEntry };
            foreach (var entry in existingEntries ?? Array.Empty<DataAuthoringReportHistoryEntry>())
            {
                if (!string.Equals(entry.Path, newEntry.Path, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(entry);
                }
            }

            return NormalizeEntries(entries, Math.Max(0, maxEntryCount));
        }

        public static string Serialize(IEnumerable<DataAuthoringReportHistoryEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (var entry in entries ?? Array.Empty<DataAuthoringReportHistoryEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.Path))
                {
                    continue;
                }

                builder
                    .Append(CurrentFormatVersion).Append('\t')
                    .Append(entry.ExportedAt.ToString("O", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.IssueCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.ChangeCount.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Encode(entry.ProfileId)).Append('\t')
                    .Append(Encode(entry.ActionKind)).Append('\t')
                    .Append(Encode(entry.OutputFolder)).Append('\t')
                    .Append(Encode(entry.Path))
                    .AppendLine();
            }

            return builder.ToString();
        }

        public static IReadOnlyList<DataAuthoringReportHistoryEntry> Deserialize(
            string value,
            string defaultProfileId,
            string defaultActionKind,
            int maxEntryCount)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<DataAuthoringReportHistoryEntry>();
            }

            var entries = new List<DataAuthoringReportHistoryEntry>();
            foreach (var line in value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var cells = line.Split('\t');
                if (TryDeserializeCurrentEntry(cells, out var entry)
                    || TryDeserializeLegacyEntry(cells, defaultProfileId, defaultActionKind, out entry))
                {
                    entries.Add(entry);
                }
            }

            return NormalizeEntries(entries, Math.Max(0, maxEntryCount));
        }

        internal static string GetOutputFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalizedPath = path.Replace('\\', '/');
            var lastSlashIndex = normalizedPath.LastIndexOf('/');
            if (lastSlashIndex <= 0)
            {
                return string.Empty;
            }

            return NormalizeFolder(normalizedPath.Substring(0, lastSlashIndex));
        }

        internal static string NormalizeFolder(string folder)
        {
            return string.IsNullOrWhiteSpace(folder) ? string.Empty : folder.Replace('\\', '/').TrimEnd('/');
        }

        private static IReadOnlyList<DataAuthoringReportHistoryEntry> NormalizeEntries(
            IEnumerable<DataAuthoringReportHistoryEntry> entries,
            int maxEntryCount)
        {
            if (maxEntryCount <= 0)
            {
                return Array.Empty<DataAuthoringReportHistoryEntry>();
            }

            return (entries ?? Array.Empty<DataAuthoringReportHistoryEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                .OrderByDescending(entry => entry.ExportedAt)
                .ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Take(maxEntryCount)
                .ToArray();
        }

        private static bool TryDeserializeCurrentEntry(string[] cells, out DataAuthoringReportHistoryEntry entry)
        {
            entry = default;
            if (cells.Length != 8
                || cells[0] != CurrentFormatVersion
                || !DateTime.TryParse(cells[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exportedAt)
                || !int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var issueCount)
                || !int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var changeCount)
                || !TryDecode(cells[4], out var profileId)
                || !TryDecode(cells[5], out var actionKind)
                || !TryDecode(cells[6], out var outputFolder)
                || !TryDecode(cells[7], out var path))
            {
                return false;
            }

            entry = new DataAuthoringReportHistoryEntry(profileId, actionKind, issueCount, changeCount, outputFolder, path, exportedAt);
            return true;
        }

        private static bool TryDeserializeLegacyEntry(
            string[] cells,
            string defaultProfileId,
            string defaultActionKind,
            out DataAuthoringReportHistoryEntry entry)
        {
            entry = default;
            if (cells.Length != 3
                || !DateTime.TryParse(cells[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exportedAt)
                || !int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var issueCount)
                || !TryDecode(cells[2], out var path))
            {
                return false;
            }

            entry = CreateEntry(path, defaultProfileId, defaultActionKind, issueCount, 0, exportedAt);
            return true;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static bool TryDecode(string value, out string decoded)
        {
            decoded = string.Empty;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
