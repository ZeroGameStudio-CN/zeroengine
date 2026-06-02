namespace ZeroEngine.World.WorldGraph
{
    public enum WorldStreamingResultStatus
    {
        Succeeded,
        GraphMissing,
        CellNotFound,
        LoaderFailed,
        ReadinessFailed,
        BudgetExceeded,
        Cancelled,
        Busy
    }
}
