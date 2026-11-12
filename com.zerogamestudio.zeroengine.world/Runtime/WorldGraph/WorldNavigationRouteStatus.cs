namespace ZeroEngine.World.WorldGraph
{
    public enum WorldNavigationRouteStatus
    {
        Succeeded = 0,
        GraphMissing = 1,
        LinkNotFound = 2,
        AnchorNotFound = 3,
        OriginMismatch = 4,
        RouteNotConnected = 5,
        NavigationUnavailable = 6
    }
}
