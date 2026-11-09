using System;

namespace ZeroEngine.StatSystem.Core
{
    /// <summary>Arithmetic only. Hosts retain their event conditions, order and non-finite diagnostics.</summary>
    public static class StatCurrentValue
    {
        public static float Clamp(float value, float minimum, float maximum)
        {
            if (value < minimum) value = minimum;
            else if (value > maximum) value = maximum;
            return value;
        }

        public static float PreserveRatioCeiling(float maximum, float previousRatio) =>
            (float)Math.Ceiling(maximum * previousRatio);

        public static float AdjustLegacyMaximum(float current, float oldMaximum,
            float newMaximum, bool preserveRatio)
        {
            if (preserveRatio && oldMaximum > 0)
            {
                float ratio = current / oldMaximum;
                current = ratio * newMaximum;
            }
            else
            {
                float delta = newMaximum - oldMaximum;
                if (delta > 0) current += delta;
            }
            return Clamp(current, 0, newMaximum);
        }
    }
}
