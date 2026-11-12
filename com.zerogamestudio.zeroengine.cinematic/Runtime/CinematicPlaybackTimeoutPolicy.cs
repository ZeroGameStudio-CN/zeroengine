namespace ZeroEngine.Cinematic
{
    public readonly struct CinematicPlaybackTimeoutPolicy
    {
        public const float DefaultMaxPlaybackSeconds = 30f;

        public CinematicPlaybackTimeoutPolicy(float maxPlaybackSeconds)
        {
            MaxPlaybackSeconds = maxPlaybackSeconds;
        }

        public float MaxPlaybackSeconds { get; }

        public static CinematicPlaybackTimeoutPolicy CreateDefault()
        {
            return new CinematicPlaybackTimeoutPolicy(DefaultMaxPlaybackSeconds);
        }
    }
}
