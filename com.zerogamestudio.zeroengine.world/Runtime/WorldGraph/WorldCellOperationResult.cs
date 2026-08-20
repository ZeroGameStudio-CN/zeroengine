namespace ZeroEngine.World.WorldGraph
{
    public enum WorldCellReadinessStatus
    {
        Succeeded,
        Failed,
        Cancelled
    }

    public readonly struct WorldCellOperationResult
    {
        private WorldCellOperationResult(WorldCellOperationStatus status, string cellId, string message)
        {
            Status = status;
            CellId = cellId;
            Message = message;
        }

        public WorldCellOperationStatus Status { get; }
        public string CellId { get; }
        public string Message { get; }
        public bool IsSuccess => Status == WorldCellOperationStatus.Succeeded;

        public static WorldCellOperationResult SucceededResult(string cellId)
        {
            return new WorldCellOperationResult(WorldCellOperationStatus.Succeeded, cellId, null);
        }

        public static WorldCellOperationResult Failed(string cellId, string message)
        {
            return new WorldCellOperationResult(WorldCellOperationStatus.Failed, cellId, message);
        }

        public static WorldCellOperationResult Cancelled(string cellId, string message = null)
        {
            return new WorldCellOperationResult(WorldCellOperationStatus.Cancelled, cellId, message);
        }
    }

    public readonly struct WorldCellReadinessResult
    {
        public WorldCellReadinessResult(
            WorldCellReadinessStatus status,
            string cellId,
            string message = null)
        {
            Status = status;
            CellId = cellId;
            Message = message;
        }

        public WorldCellReadinessStatus Status { get; }
        public string CellId { get; }
        public string Message { get; }
        public bool IsSuccess => Status == WorldCellReadinessStatus.Succeeded;

        public static WorldCellReadinessResult SucceededResult(string cellId)
        {
            return new WorldCellReadinessResult(WorldCellReadinessStatus.Succeeded, cellId);
        }

        public static WorldCellReadinessResult Failed(string cellId, string message)
        {
            return new WorldCellReadinessResult(WorldCellReadinessStatus.Failed, cellId, message);
        }

        public static WorldCellReadinessResult Cancelled(string cellId, string message)
        {
            return new WorldCellReadinessResult(WorldCellReadinessStatus.Cancelled, cellId, message);
        }
    }
}
