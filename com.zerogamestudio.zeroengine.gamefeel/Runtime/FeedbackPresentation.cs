using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>Two independent instance multipliers; resetting a pooled output restores the neutral product.</summary>
    public struct FeedbackScaleState
    {
        private float profile, coordinated;
        private byte initialized;
        public static FeedbackScaleState Neutral => default;
        public float Multiplier => ((initialized & 1) == 0 ? 1 : profile) * ((initialized & 2) == 0 ? 1 : coordinated);
        public void SetProfile(float value) { profile = Finite(value) ? value : 1; initialized |= 1; }
        public void SetCoordinated(float value) { coordinated = Finite(value) ? value : 1; initialized |= 2; }
        public void Reset() => this = default;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public enum FeedbackMixingMode { Additive, Replace, KeepStrongest }

    /// <summary>Shared overlap rules in the output's own amplitude units, without a time-based cooldown.</summary>
    public static class FeedbackMixing
    {
        public static bool ShouldReplace(FeedbackMixingMode mode, float candidate, float strongest) =>
            mode == FeedbackMixingMode.Replace || (mode == FeedbackMixingMode.KeepStrongest && candidate > strongest);
        public static bool CanAccept(FeedbackMixingMode mode, float candidate, float strongest) =>
            candidate > 0 && (mode != FeedbackMixingMode.KeepStrongest || candidate > strongest);
        public static float ResolveStrength(float requested, float activeTotal, float maximum)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested <= 0 ||
                float.IsNaN(maximum) || float.IsInfinity(maximum)) return 0;
            if (maximum <= 0) return requested;
            return Math.Min(requested, Math.Max(0, maximum - Math.Max(0, activeTotal)));
        }
    }

    /// <summary>Unit-preserving helpers. They do not choose a project's colors, authored amplitudes or clocks.</summary>
    public static class FeedbackPresentation
    {
        public static float ScaleAmplitude(float requested, float scale) => requested * SafeClamp(scale, 0, 1, 1);
        public static float TighterPositiveLimit(float authored, float coordinated) =>
            coordinated > 0 ? authored > 0 ? Math.Min(authored, coordinated) : coordinated : authored;
        public static float EnvelopeScale(float scale) => SafeClamp(scale, .1f, 1, 1);
        public static float AttenuateExcess(float value, float strength) =>
            value > 1 ? 1 + (value - 1) * SafeClamp(strength, 0, 1, 1) : value;
        private static float SafeClamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    }
}
