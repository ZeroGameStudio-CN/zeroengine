using System;
using System.Collections.Generic;

namespace ZeroEngine
{
    /// <summary>Pure weighted selection. Callers own candidate order, RNG and gameplay policy.</summary>
    public static class WeightedSelection
    {
        public static bool TrySelectIndex<T>(IReadOnlyList<T> items, Func<T, double> weight,
            double unit, out int index)
        {
            index = -1;
            if (items == null || weight == null || !IsFinite(unit) || unit < 0 || unit >= 1) return false;
            var weights = new double[items.Count];
            double maximum = 0;
            for (int i = 0; i < items.Count; i++)
            {
                double value = weight(items[i]);
                if (!IsFinite(value) || value < 0) return false;
                weights[i] = value;
                maximum = Math.Max(maximum, value);
            }
            if (maximum == 0) return false;
            double total = 0;
            foreach (double value in weights) total += value / maximum;
            double cursor = unit * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                index = i;
                cursor -= weights[i] / maximum;
                if (cursor < 0) return true;
            }
            // Floating-point rounding can reach the final positive interval's upper edge.
            return index >= 0;
        }

        /// <summary>Shared logarithmic luck curve for any number of ordered rarity ranks.</summary>
        public static double LuckMultiplier(int rank, int rankCount, double luck,
            double scale, double strength)
        {
            if (rankCount < 1 || rank < 0 || rank >= rankCount || !IsFinite(luck)
                || !IsFinite(scale) || scale <= 0 || !IsFinite(strength) || strength < 0)
                throw new ArgumentOutOfRangeException(nameof(rank), "Invalid rarity or luck curve parameters.");
            if (rankCount == 1 || luck == 0 || strength == 0) return 1;
            double centered = (double)rank / (rankCount - 1) - 0.5;
            if (centered == 0) return 1;
            double magnitude = Math.Abs(luck);
            // Equivalent log(1 + magnitude / scale), without overflowing the ratio.
            double logarithm = magnitude > scale
                ? Math.Log(magnitude) - Math.Log(scale) + Math.Log(1 + scale / magnitude)
                : Math.Log(1 + magnitude / scale);
            double exponent = strength * (Math.Sign(luck) * logarithm * centered);
            return Math.Exp(Math.Max(-80, Math.Min(80, exponent)));
        }

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
