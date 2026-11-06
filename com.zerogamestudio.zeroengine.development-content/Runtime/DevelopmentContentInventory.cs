using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ZeroEngine.DevelopmentContent
{
    // Keys are opaque adapter identities. Paths are project-relative, never commands to execute.
    public sealed class DevelopmentContentInventoryEntry
    {
        public string Key { get; }
        public string Group { get; }
        public string Title { get; }
        public string Entrance { get; }
        public string Boundary { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Links { get; }

        public DevelopmentContentInventoryEntry(string key, string group, string title, string entrance,
            string boundary, IEnumerable<KeyValuePair<string, string>> links = null)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Inventory key is required.", nameof(key));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Inventory title is required.", nameof(title));
            Key = key;
            Group = group ?? string.Empty;
            Title = title;
            Entrance = entrance ?? string.Empty;
            Boundary = boundary ?? string.Empty;
            Links = Array.AsReadOnly((links ?? Array.Empty<KeyValuePair<string, string>>()).ToArray());
        }

        public bool Matches(string query) => string.IsNullOrWhiteSpace(query) ||
            string.Join(" ", Group, Title, Entrance, Boundary).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public interface IDevelopmentContentInventory
    {
        IReadOnlyList<DevelopmentContentInventoryEntry> Entries { get; }
        string Error { get; }
        void Refresh();
    }

    public sealed class DevelopmentContentRow
    {
        public DevelopmentContentRecord Record { get; }
        public DevelopmentContentInventoryEntry Source { get; }
        public string Key => Record?.Id ?? "source:" + Source.Key;
        public string Title => Record?.Title ?? Source.Title;
        public bool InView(DevelopmentContentPlan plan, ContentPlanView view) => Record == null
            ? view is ContentPlanView.Unplanned or ContentPlanView.All : plan.InView(Record, view);
        public bool Matches(string query) => string.IsNullOrWhiteSpace(query) || Source?.Matches(query) == true ||
            string.Join(" ", Title, Record?.Id, Record?.ResponsibleChat, Record?.NextAction, Record?.Entrance, Record?.ResourcePath)
                .IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private DevelopmentContentRow(DevelopmentContentRecord record, DevelopmentContentInventoryEntry source)
        { Record = record; Source = source; }

        public static DevelopmentContentRow[] Merge(DevelopmentContentPlan plan, IEnumerable<DevelopmentContentInventoryEntry> inventory)
        {
            var entries = inventory.ToArray();
            var linked = plan.Records.Where(record => !string.IsNullOrEmpty(record.SourceKey)).Select(record => record.SourceKey).ToHashSet(StringComparer.Ordinal);
            return plan.Records.Select(record => new DevelopmentContentRow(record, entries.FirstOrDefault(entry => entry.Key == record.SourceKey)))
                .Concat(entries.Where(entry => !linked.Contains(entry.Key)).Select(entry => new DevelopmentContentRow(null, entry))).ToArray();
        }
    }

    public static class DevelopmentContentPaths
    {
        public static string ResolveProjectPath(string projectRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
                Uri.TryCreate(relativePath, UriKind.Absolute, out _)) return null;
            try
            {
                string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string path = Path.GetFullPath(Path.Combine(root, relativePath));
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return path.StartsWith(root, comparison) ? path : null;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            { return null; }
        }
    }
}
