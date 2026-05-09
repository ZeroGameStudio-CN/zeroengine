namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformNavigationSnapshot
    {
        public readonly bool HasGraphGenerator;
        public readonly bool IsGenerated;
        public readonly int NodeCount;
        public readonly int LinkCount;
        public readonly bool HasValidPath;
        public readonly int CommandCount;
        public readonly int CurrentCommandIndex;
        public readonly PlatformPathFailureReason LastFailureReason;
        public readonly PlatformPathCompletionKind CompletionKind;
        public readonly string CommandDebug;

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
            string commandDebug)
        {
            HasGraphGenerator = hasGraphGenerator;
            IsGenerated = isGenerated;
            NodeCount = nodeCount;
            LinkCount = linkCount;
            HasValidPath = hasValidPath;
            CommandCount = commandCount;
            CurrentCommandIndex = currentCommandIndex;
            LastFailureReason = lastFailureReason;
            CompletionKind = completionKind;
            CommandDebug = commandDebug;
        }
    }
}
