namespace ZeroEngine.World.WorldGraph
{
    public enum WorldCellReadinessStatus
    {
        Unknown = -1,
        Succeeded = 0,
        Failed = 1,
        Cancelled = 2
    }

    public readonly struct WorldCellOperationResult
    {
        private readonly WorldCellOperationStatus _status;
        private readonly bool _initialized;

        private WorldCellOperationResult(WorldCellOperationStatus status, string cellId, string message)
        {
            _status = status;
            _initialized = true;
            CellId = cellId;
            Message = message;
        }

        // 保留已发布枚举数值，同时禁止默认 struct 被误判为成功。
        public WorldCellOperationStatus Status => _initialized ? _status : WorldCellOperationStatus.Unknown;
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
        private readonly WorldCellReadinessStatus _status;
        private readonly bool _initialized;

        public WorldCellReadinessResult(
            WorldCellReadinessStatus status,
            string cellId,
            string message = null)
        {
            _status = status;
            _initialized = true;
            CellId = cellId;
            Message = message;
        }

        public WorldCellReadinessStatus Status => _initialized ? _status : WorldCellReadinessStatus.Unknown;
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
