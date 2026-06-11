using System;

namespace ZeroEngine.ModSystem
{
    [Serializable]
    public sealed class ModManifest
    {
        public string Id;
        public string Name;
        public string Version;
        public string Author;
        public string Description;
        public string[] Dependencies;
        public string[] Conflicts;
        public string GameVersion;
        public string[] ContentPaths;
        public string[] TceGraphs;
        public ModContentSet[] ContentSets;
        public bool Enabled = true;

        [NonSerialized] public string RootPath;
        [NonSerialized] public int LoadOrder;

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                error = "Mod manifest Id must not be empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                error = "Mod manifest Name must not be empty.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public string[] GetContentPaths(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || ContentSets == null)
                return Array.Empty<string>();

            foreach (var set in ContentSets)
            {
                if (set != null && string.Equals(set.Key, key, StringComparison.Ordinal))
                    return set.Paths ?? Array.Empty<string>();
            }

            return Array.Empty<string>();
        }

        public override string ToString() => $"{Id} v{Version}";
    }

    [Serializable]
    public sealed class ModContentSet
    {
        public string Key;
        public string[] Paths;
    }
}
