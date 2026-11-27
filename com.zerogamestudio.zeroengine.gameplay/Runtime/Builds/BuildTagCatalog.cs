using System;
using System.Collections.Generic;

namespace ZeroEngine.Builds
{
    /// <summary>A game's stable tag IDs, independent of localized display names.</summary>
    public sealed class BuildTagCatalog
    {
        internal static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
        private readonly Dictionary<string, string> ids = new(Comparer);

        public BuildTagCatalog(IEnumerable<string> ids)
        {
            if (ids == null) return;

            foreach (string value in ids)
            {
                string id = Normalize(value);
                if (id != null)
                    this.ids.TryAdd(id, id);
            }
        }

        public IReadOnlyCollection<string> Ids => ids.Values;

        public bool TryResolve(string value, out string id)
        {
            id = null;
            string key = Normalize(value);
            return key != null && ids.TryGetValue(key, out id);
        }

        internal static string Normalize(string id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }
}
