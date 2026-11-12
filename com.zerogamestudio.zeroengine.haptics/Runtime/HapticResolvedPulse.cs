namespace ZeroEngine.Haptics
{
    /// <summary>A bounded dual-motor pulse ready for a platform adapter.</summary>
    public readonly struct HapticResolvedPulse
    {
        internal HapticResolvedPulse(
            float lowFrequencyMotor,
            float highFrequencyMotor,
            float duration,
            float strength)
        {
            LowFrequencyMotor = lowFrequencyMotor;
            HighFrequencyMotor = highFrequencyMotor;
            Duration = duration;
            Strength = strength;
            Energy = duration *
                     (lowFrequencyMotor * lowFrequencyMotor
                      + highFrequencyMotor * highFrequencyMotor);
        }

        public float LowFrequencyMotor { get; }
        public float HighFrequencyMotor { get; }
        public float Duration { get; }
        public float Strength { get; }
        public float Energy { get; }
        public bool IsPlayable => Strength > 0f && Duration > 0f;

        internal HapticResolvedPulse Scale(float scale)
        {
            if (scale <= 0f) return default;
            if (scale >= 1f) return this;

            return new HapticResolvedPulse(
                LowFrequencyMotor * scale,
                HighFrequencyMotor * scale,
                Duration,
                Strength * scale);
        }
    }
}
