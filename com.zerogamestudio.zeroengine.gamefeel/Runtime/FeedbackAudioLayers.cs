using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>One reusable layer recipe for single and clustered contact batches; clips remain sink-owned.</summary>
    public readonly struct FeedbackAudioSettings
    {
        public readonly float BaseVolume, MaximumVolume, MinimumPitch, CurvePower;
        public readonly int MaximumHits;
        public FeedbackAudioSettings(float baseVolume, float maximumVolume, float minimumPitch, float curvePower, int maximumHits)
        {
            BaseVolume = Clamp(baseVolume, 0, 1);
            MaximumVolume = Clamp(maximumVolume, BaseVolume, 1);
            MinimumPitch = Clamp(minimumPitch, .01f, 1);
            CurvePower = Finite(curvePower) ? Math.Max(.1f, curvePower) : 1;
            MaximumHits = Math.Max(2, maximumHits);
        }
        public static FeedbackAudioSettings Default => new FeedbackAudioSettings(.65f, 1, .85f, 1, 16);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Clamp(float value, float min, float max) => Finite(value) ? Math.Min(max, Math.Max(min, value)) : min;
    }

    /// <summary>Allocation-free audio envelope decisions, suitable for pooled or native voice adapters.</summary>
    public readonly struct FeedbackAudioLayers
    {
        public readonly float Volume, Pitch, SecondVolume, SecondPitch, ThirdVolume, ThirdPitch, PulseSpacing;
        public readonly float ClusterVolume, ClusterPitch, WeightVolume, WeightPitch;
        public readonly int ExtraPulses;
        public readonly bool HasCluster, HasWeight;

        private FeedbackAudioLayers(float volume, float pitch, int pulses, float secondVolume, float secondPitch,
            float thirdVolume, float thirdPitch, float spacing, float clusterVolume, float clusterPitch, float weightVolume, float weightPitch,
            bool hasCluster, bool hasWeight)
        {
            Volume = volume; Pitch = pitch; ExtraPulses = pulses;
            SecondVolume = secondVolume; SecondPitch = secondPitch; ThirdVolume = thirdVolume; ThirdPitch = thirdPitch;
            PulseSpacing = spacing; ClusterVolume = clusterVolume; ClusterPitch = clusterPitch;
            WeightVolume = weightVolume; WeightPitch = weightPitch;
            HasCluster = hasCluster; HasWeight = hasWeight;
        }

        public static FeedbackAudioLayers Evaluate(int contacts, float requestedVolume, float basePitch,
            double windowSeconds, FeedbackAudioSettings settings, bool clusterAvailable = false, bool weightAvailable = false)
        {
            contacts = Math.Max(1, contacts);
            // A default(struct) recipe is a valid neutral fallback, not a zero-hit logarithm.
            if (settings.MaximumHits < 2) settings = FeedbackAudioSettings.Default;
            requestedVolume = FeedbackAudioSettings.Finite(requestedVolume) ? Math.Max(0, requestedVolume) : 0;
            basePitch = Clamp(basePitch, .01f, 3);
            float raw = Clamp((float)Math.Log(contacts) / (float)Math.Log(settings.MaximumHits), 0, 1);
            float strength = (float)Math.Pow(raw, settings.CurvePower);
            float volume = Math.Min(1, requestedVolume * Lerp(settings.BaseVolume, settings.MaximumVolume, strength));
            float pitch = Clamp(basePitch * Lerp(1, settings.MinimumPitch, strength), .01f, 3);
            int secondThreshold = Math.Max(2, (int)Math.Ceiling(settings.MaximumHits * .25f));
            int thirdThreshold = Math.Max(secondThreshold + 1, (int)Math.Ceiling(settings.MaximumHits * .5f));
            int pulses = contacts >= thirdThreshold ? 2 : contacts >= secondThreshold ? 1 : 0;
            float secondStrength = Fraction(secondThreshold, settings.MaximumHits, Math.Min(contacts, settings.MaximumHits));
            float secondVolume = pulses >= 1 ? Math.Min(1, volume * Lerp(.5f, .65f, secondStrength)) : 0;
            float secondPitch = Clamp(basePitch * Lerp(1.04f, .97f, secondStrength), .01f, 3);
            float thirdStrength = Fraction(thirdThreshold, settings.MaximumHits, Math.Min(contacts, settings.MaximumHits));
            float thirdVolume = pulses >= 2 ? Math.Min(1, volume * Lerp(.36f, .5f, thirdStrength)) : 0;
            float thirdPitch = Clamp(basePitch * Lerp(.93f, .86f, thirdStrength), .01f, 3);
            float spacing = Clamp((float)windowSeconds * .7f, .02f, .05f);
            float clusterVolume = 0, clusterPitch = basePitch, weightVolume = 0, weightPitch = basePitch;
            if (clusterAvailable && contacts >= secondThreshold)
            {
                clusterVolume = Math.Min(1, volume * Lerp(.3f, .55f, secondStrength));
                clusterPitch = Clamp(basePitch * Lerp(1.06f, .98f, secondStrength), .01f, 3);
            }
            int weightThreshold = Math.Max(secondThreshold, (int)Math.Ceiling(settings.MaximumHits * .5f));
            if (weightAvailable && contacts >= weightThreshold)
            {
                float weightStrength = Fraction(weightThreshold, settings.MaximumHits, Math.Min(contacts, settings.MaximumHits));
                weightVolume = Math.Min(1, volume * Lerp(.25f, .45f, weightStrength));
                weightPitch = Clamp(basePitch * Lerp(.92f, settings.MinimumPitch, weightStrength), .01f, 3);
            }
            return new FeedbackAudioLayers(volume, pitch, pulses, secondVolume, secondPitch, thirdVolume, thirdPitch,
                spacing, clusterVolume, clusterPitch, weightVolume, weightPitch,
                clusterAvailable && contacts >= secondThreshold, weightAvailable && contacts >= weightThreshold);
        }

        private static float Clamp(float value, float min, float max) => FeedbackAudioSettings.Clamp(value, min, max);
        private static float Lerp(float from, float to, float strength) => from + (to - from) * Clamp(strength, 0, 1);
        private static float Fraction(float min, float max, float value) => min == max ? 0 : Clamp((value - min) / (max - min), 0, 1);
    }
}
