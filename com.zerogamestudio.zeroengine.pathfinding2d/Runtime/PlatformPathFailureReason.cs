namespace ZeroEngine.Pathfinding2D
{
    public enum PlatformPathFailureReason
    {
        None,
        Throttled,
        MissingGraphGenerator,
        GraphNotGenerated,
        AlreadyArrived,
        StartNodeNotFound,
        EndNodeNotFound,
        PathNotFound,
        PartialPath,
        PartialPathUnavailable,
        InvalidCommand,
        BackendUnavailable,
        BackendTimeout,
        BackendFault,
        StaleResult,
        Cancelled
    }
}
