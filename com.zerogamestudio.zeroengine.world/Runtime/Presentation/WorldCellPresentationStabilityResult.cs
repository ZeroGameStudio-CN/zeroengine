namespace ZeroEngine.World.Presentation
{
    public enum WorldCellPresentationStabilityStatus
    {
        Unknown = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3
    }

    public readonly struct WorldCellPresentationStabilityResult
    {
        private readonly string _cellId;
        private readonly string _message;

        public WorldCellPresentationStabilityResult(
            WorldCellPresentationStabilityStatus status,
            string cellId,
            string message = null)
        {
            Status = status;
            _cellId = cellId?.Trim() ?? string.Empty;
            _message = message?.Trim() ?? string.Empty;
            if (status == WorldCellPresentationStabilityStatus.Succeeded
                && string.IsNullOrEmpty(_cellId))
            {
                throw new System.ArgumentException(
                    "A successful presentation stability result requires a cell identifier.",
                    nameof(cellId));
            }
        }

        public WorldCellPresentationStabilityStatus Status { get; }
        public string CellId => _cellId ?? string.Empty;
        public string Message => _message ?? string.Empty;
        public bool IsSuccess => Status == WorldCellPresentationStabilityStatus.Succeeded
                                 && !string.IsNullOrEmpty(CellId);

        public static WorldCellPresentationStabilityResult SucceededResult(string cellId)
        {
            return new WorldCellPresentationStabilityResult(
                WorldCellPresentationStabilityStatus.Succeeded,
                RequireCellId(cellId));
        }

        public static WorldCellPresentationStabilityResult Failed(string cellId, string message)
        {
            return new WorldCellPresentationStabilityResult(
                WorldCellPresentationStabilityStatus.Failed,
                RequireCellId(cellId),
                message);
        }

        public static WorldCellPresentationStabilityResult Cancelled(
            string cellId,
            string message = null)
        {
            return new WorldCellPresentationStabilityResult(
                WorldCellPresentationStabilityStatus.Cancelled,
                RequireCellId(cellId),
                message);
        }

        private static string RequireCellId(string cellId)
        {
            var normalized = cellId?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new System.ArgumentException(
                    "A presentation stability result requires a cell identifier.",
                    nameof(cellId));
            }

            return normalized;
        }
    }
}
