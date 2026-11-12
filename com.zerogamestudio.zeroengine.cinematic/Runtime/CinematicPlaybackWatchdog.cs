namespace ZeroEngine.Cinematic
{
    public sealed class CinematicPlaybackWatchdog
    {
        private readonly CinematicPlaybackTimeoutPolicy _timeoutPolicy;

        public CinematicPlaybackWatchdog(CinematicPlaybackTimeoutPolicy timeoutPolicy)
        {
            _timeoutPolicy = timeoutPolicy;
        }

        public CinematicPlayResult Evaluate(float elapsedSeconds, bool isPlaying)
        {
            if (!isPlaying || _timeoutPolicy.MaxPlaybackSeconds <= 0f || elapsedSeconds <= _timeoutPolicy.MaxPlaybackSeconds)
            {
                return CinematicPlayResult.None;
            }

            return new CinematicPlayResult(
                CinematicPlayStatus.TimedOut,
                $"Cinematic playback timed out after {elapsedSeconds:0.###} seconds.",
                requiresAbortCleanup: true);
        }
    }
}
