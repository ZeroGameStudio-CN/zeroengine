namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicPlayResult
    {
        public CinematicPlayResult(
            CinematicPlayStatus status,
            string message = "",
            bool requiresAbortCleanup = false,
            string sequenceId = "")
        {
            Status = status;
            SequenceId = string.IsNullOrWhiteSpace(sequenceId) ? string.Empty : sequenceId.Trim();
            Message = message ?? string.Empty;
            RequiresAbortCleanup = requiresAbortCleanup;
        }

        public CinematicPlayStatus Status { get; }

        public string SequenceId { get; }

        public string Message { get; }

        public bool RequiresAbortCleanup { get; }

        public bool Succeeded =>
            Status == CinematicPlayStatus.Completed ||
            Status == CinematicPlayStatus.SkippedCompleted;

        public static CinematicPlayResult None { get; } = new(CinematicPlayStatus.None);

        public CinematicPlayResult WithSequenceId(string sequenceId)
        {
            return new CinematicPlayResult(
                Status,
                Message,
                RequiresAbortCleanup,
                sequenceId);
        }
    }
}
