namespace ZeroEngine.World.WorldGraph
{
    public readonly struct WorldNavigationRouteResult
    {
        public WorldNavigationRouteResult(
            WorldNavigationRouteStatus status,
            WorldPosition from,
            WorldPosition to,
            string linkId = null,
            string message = null)
        {
            Status = status;
            From = from;
            To = to;
            LinkId = linkId;
            Message = message;
        }

        public WorldNavigationRouteStatus Status { get; }
        public WorldPosition From { get; }
        public WorldPosition To { get; }
        public string LinkId { get; }
        public string Message { get; }
        public bool Succeeded => Status == WorldNavigationRouteStatus.Succeeded;
    }
}
