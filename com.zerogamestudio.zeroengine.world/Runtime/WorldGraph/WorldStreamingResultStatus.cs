namespace ZeroEngine.World.WorldGraph
{
    public enum WorldStreamingResultStatus
    {
        Unknown = -1,
        Succeeded = 0,
        GraphMissing = 1,
        CellNotFound = 2,
        LoaderFailed = 3,
        ReadinessFailed = 4,
        BudgetExceeded = 5,
        Cancelled = 6,
        Busy = 7,
        LayerMismatch = 8,
        RollbackFailed = 9
    }
}
