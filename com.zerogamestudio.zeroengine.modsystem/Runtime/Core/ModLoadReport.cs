using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    public sealed class ModLoadReport
    {
        public ModLoadReport(IReadOnlyList<ModManifest> loadedManifests, IReadOnlyList<ModLoadIssue> issues)
        {
            LoadedManifests = loadedManifests ?? new List<ModManifest>();
            Issues = issues ?? new List<ModLoadIssue>();
        }

        public IReadOnlyList<ModManifest> LoadedManifests { get; }
        public IReadOnlyList<ModLoadIssue> Issues { get; }
    }
}
