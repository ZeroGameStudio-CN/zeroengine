using System;

namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldGraphRuntimeSessionResult
    {
        public WorldGraphRuntimeSessionResult(
            WorldGraphRuntimeSessionStatus status,
            WorldStreamingResult streamingResult = default,
            WorldTravelResult travelResult = default,
            WorldGraphRuntimeLocation location = default,
            Exception exception = null)
        {
            Status = status;
            StreamingResult = streamingResult;
            TravelResult = travelResult;
            Location = location;
            Exception = exception;
        }

        public WorldGraphRuntimeSessionStatus Status { get; }
        public WorldStreamingResult StreamingResult { get; }
        public WorldTravelResult TravelResult { get; }
        public WorldGraphRuntimeLocation Location { get; }
        public Exception Exception { get; }

        public bool Succeeded =>
            Status == WorldGraphRuntimeSessionStatus.Loaded
            || Status == WorldGraphRuntimeSessionStatus.Unloaded
            || Status == WorldGraphRuntimeSessionStatus.Traveled;
    }
}
