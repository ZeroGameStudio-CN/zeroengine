using System;
using System.Collections.Generic;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldGraphRuntimeSnapshot
    {
        public WorldGraphRuntimeSnapshot(
            string worldGraphId,
            string activeCellId,
            IReadOnlyList<string> loadedCellIds,
            IReadOnlyList<string> pinnedCellSummaries,
            string runtimeState,
            string lastFailure)
        {
            WorldGraphId = worldGraphId ?? string.Empty;
            ActiveCellId = activeCellId ?? string.Empty;
            LoadedCellIds = loadedCellIds ?? Array.Empty<string>();
            PinnedCellSummaries = pinnedCellSummaries ?? Array.Empty<string>();
            RuntimeState = runtimeState ?? string.Empty;
            LastFailure = lastFailure ?? string.Empty;
        }

        public string WorldGraphId { get; }
        public string ActiveCellId { get; }
        public IReadOnlyList<string> LoadedCellIds { get; }
        public IReadOnlyList<string> PinnedCellSummaries { get; }
        public string RuntimeState { get; }
        public string LastFailure { get; }
        public bool HasWorldGraph => !string.IsNullOrWhiteSpace(WorldGraphId);
    }
}
