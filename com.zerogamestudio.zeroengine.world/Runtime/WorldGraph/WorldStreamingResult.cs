using System;
using System.Collections.Generic;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldStreamingResult
    {
        private readonly WorldStreamingResultStatus _status;
        private readonly bool _initialized;

        public WorldStreamingResult(
            WorldStreamingResultStatus status,
            string activeCellId,
            IReadOnlyList<string> loadedCellIds,
            string message = null)
        {
            _status = status;
            _initialized = true;
            ActiveCellId = activeCellId;
            LoadedCellIds = loadedCellIds ?? Array.Empty<string>();
            Message = message;
        }

        public WorldStreamingResultStatus Status => _initialized ? _status : WorldStreamingResultStatus.Unknown;
        public string ActiveCellId { get; }
        public IReadOnlyList<string> LoadedCellIds { get; }
        public string Message { get; }
        public bool Succeeded => Status == WorldStreamingResultStatus.Succeeded;
    }
}
