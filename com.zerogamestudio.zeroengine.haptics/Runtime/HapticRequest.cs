namespace ZeroEngine.Haptics
{
    /// <summary>
    /// Platform-independent haptic intent. Values may be authored on any non-negative scale;
    /// the resolver applies a fixed soft-saturating safety response.
    /// </summary>
    public readonly struct HapticRequest
    {
        public HapticRequest(float intensity, float sharpness, float sourceDuration, float gain = 1f)
        {
            Intensity = intensity;
            Sharpness = sharpness;
            SourceDuration = sourceDuration;
            Gain = gain;
        }

        public float Intensity { get; }
        public float Sharpness { get; }
        public float SourceDuration { get; }
        public float Gain { get; }
    }
}
