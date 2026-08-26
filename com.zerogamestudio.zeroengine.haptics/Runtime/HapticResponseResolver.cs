using System;

namespace ZeroEngine.Haptics
{
    /// <summary>Deterministic, platform-independent haptic response mapping.</summary>
    public static class HapticResponseResolver
    {
        public const float MaximumStrength = 0.40f;
        public const float MaximumLowFrequencyMotor = 0.40f;
        public const float MaximumHighFrequencyMotor = 0.36f;
        public const float MaximumDuration = 0.18f;
        public const float MinimumPlayableStrength = 0.03f;

        private const double IntensityHalfSaturation = 0.50;
        private const double SharpnessHalfSaturation = 2.50;
        private const double MinimumDuration = 0.045;
        private const double DurationRange = 0.135;
        private const double DurationHalfSaturation = 0.25;

        public static HapticResolvedPulse Resolve(HapticRequest request)
        {
            return TryResolve(request, out HapticResolvedPulse pulse) ? pulse : default;
        }

        public static bool TryResolve(HapticRequest request, out HapticResolvedPulse pulse)
        {
            pulse = default;
            if (!IsFinite(request.Intensity)
                || !IsFinite(request.Sharpness)
                || !IsFinite(request.SourceDuration)
                || !IsFinite(request.Gain))
            {
                return false;
            }

            double intensity = Math.Max(0d, request.Intensity);
            double gain = Math.Max(0d, request.Gain);
            if (intensity <= 0d || gain <= 0d) return false;

            double scaledIntensity = intensity * gain;
            double level = MaximumStrength
                           * SoftSaturate(scaledIntensity, IntensityHalfSaturation);
            if (level < MinimumPlayableStrength) return false;

            double sharpness = Math.Max(0d, request.Sharpness);
            double tone = SoftSaturate(sharpness, SharpnessHalfSaturation);
            double lowFactor = 1d - 0.45d * tone;
            double highFactor = 0.20d + 0.70d * tone;

            double sourceDuration = Math.Max(0d, request.SourceDuration);
            double duration = MinimumDuration
                              + DurationRange
                              * SoftSaturate(sourceDuration, DurationHalfSaturation);

            float strength = Clamp((float)level, 0f, MaximumStrength);
            float low = Clamp(
                (float)(level * lowFactor),
                0f,
                MaximumLowFrequencyMotor);
            float high = Clamp(
                (float)(level * highFactor),
                0f,
                MaximumHighFrequencyMotor);
            float boundedDuration = Clamp((float)duration, 0f, MaximumDuration);

            pulse = new HapticResolvedPulse(low, high, boundedDuration, strength);
            return pulse.IsPlayable;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static double SoftSaturate(double value, double halfSaturation)
        {
            return value <= 0d ? 0d : value / (value + halfSaturation);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (value <= minimum) return minimum;
            return value >= maximum ? maximum : value;
        }
    }
}
