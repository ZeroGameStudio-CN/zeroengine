namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldTravelResult
    {
        public WorldTravelResult(
            WorldTravelResultStatus status,
            WorldPosition destination,
            string message = null)
        {
            Status = status;
            Destination = destination;
            Message = message;
        }

        public WorldTravelResultStatus Status { get; }
        public WorldPosition Destination { get; }
        public string Message { get; }
        public bool Succeeded => Status == WorldTravelResultStatus.Succeeded;
    }
}
