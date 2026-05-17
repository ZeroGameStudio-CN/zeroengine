namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformNavigationSnapshot
    {
        public readonly bool HasGraphGenerator;
        public readonly bool IsGenerated;
        public readonly int NodeCount;
        public readonly int LinkCount;
        public readonly int SurfaceSegmentCount;
        public readonly bool HasValidPath;
        public readonly int CommandCount;
        public readonly int CurrentCommandIndex;
        public readonly PlatformPathFailureReason LastFailureReason;
        public readonly PlatformPathCompletionKind CompletionKind;
        public readonly string CommandDebug;
        public readonly string SurfaceSegmentDebug;
        public readonly float RouteCost;
        public readonly int CurrentSurfaceSegmentId;
        public readonly int TargetSurfaceSegmentId;
        public readonly bool CommandsValidated;

        public PlatformNavigationSnapshot(
            bool hasGraphGenerator,
            bool isGenerated,
            int nodeCount,
            int linkCount,
            bool hasValidPath,
            int commandCount,
            int currentCommandIndex,
            PlatformPathFailureReason lastFailureReason,
            PlatformPathCompletionKind completionKind,
            string commandDebug,
            int surfaceSegmentCount = 0,
            string surfaceSegmentDebug = null,
            float routeCost = 0f,
            int currentSurfaceSegmentId = -1,
            int targetSurfaceSegmentId = -1,
            bool commandsValidated = false)
        {
            HasGraphGenerator = hasGraphGenerator;
            IsGenerated = isGenerated;
            NodeCount = nodeCount;
            LinkCount = linkCount;
            SurfaceSegmentCount = surfaceSegmentCount;
            HasValidPath = hasValidPath;
            CommandCount = commandCount;
            CurrentCommandIndex = currentCommandIndex;
            LastFailureReason = lastFailureReason;
            CompletionKind = completionKind;
            CommandDebug = commandDebug;
            SurfaceSegmentDebug = surfaceSegmentDebug ?? "none";
            RouteCost = routeCost;
            CurrentSurfaceSegmentId = currentSurfaceSegmentId;
            TargetSurfaceSegmentId = targetSurfaceSegmentId;
            CommandsValidated = commandsValidated;
        }
    }
}
