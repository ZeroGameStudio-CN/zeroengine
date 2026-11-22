using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>Normalized reaction curves. The host controls the playback clock, direction and amplitude.</summary>
    public static class ImpactEnvelopes
    {
        public static float DirectionalRecoil(float progress)
        {
            if (!(progress > 0f) || progress >= 1f) return 0f;
            if (progress < .16f) return Smooth(progress / .16f);
            if (progress < .44f) return 1f - Smooth((progress - .16f) / .28f);
            if (progress < .62f) return -.21f * Smooth((progress - .44f) / .18f);
            return -.21f * (1f - Smooth((progress - .62f) / .38f));
        }

        public static float FlashRemaining(float progress)
        {
            if (float.IsNaN(progress)) return 0f;
            float remaining = 1f - Math.Clamp(progress, 0f, 1f);
            return remaining * remaining * remaining;
        }

        public static float CameraImpulse(float progress, float cycles)
        {
            if (!(progress > 0f) || progress >= 1f || !float.IsFinite(cycles)) return 0f;
            float phase = Math.Clamp(cycles, .5f, 4f) * progress * 2f * (float)Math.PI;
            return (float)Math.Sin(phase) * FlashRemaining(progress);
        }

        private static float Smooth(float progress) => progress * progress * (3f - 2f * progress);
    }
}
