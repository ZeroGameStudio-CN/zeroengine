using System;
using System.Collections.Generic;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldStreamingResult
    {
        public WorldStreamingResult(
            WorldStreamingResultStatus status,
            string activeCellId,
            IReadOnlyList<string> loadedCellIds,
            string message = null)
        {
            Status = status;
            ActiveCellId = activeCellId;
            LoadedCellIds = loadedCellIds ?? Array.Empty<string>();
            Message = message;
        }

        public WorldStreamingResultStatus Status { get; }
        public string ActiveCellId { get; }
        public IReadOnlyList<string> LoadedCellIds { get; }
        public string Message { get; }
        public bool Succeeded => Status == WorldStreamingResultStatus.Succeeded;
    }
}
