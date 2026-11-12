namespace ZeroEngine.Cinematic
{
    public enum CinematicPlayStatus
    {
        None = 0,
        Started = 1,
        Completed = 2,
        SkippedCompleted = 3,
        Cancelled = 4,
        Aborted = 5,
        Failed = 6,
        SequenceMissing = 7,
        BindingMissing = 8,
        AlreadyPlaying = 9,
        TimedOut = 10,
        SkipNotAllowed = 11
    }
}
