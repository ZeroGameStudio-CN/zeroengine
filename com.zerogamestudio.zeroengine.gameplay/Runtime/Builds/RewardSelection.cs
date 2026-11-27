using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Builds
{
    public delegate bool TrySelectReward<T>(IReadOnlyList<T> candidates, out T selected);

    /// <summary>Composes tier and candidate draws using only the weights and random source of the caller.</summary>
    public static class RewardSelection
    {
        public static bool TrySelectTiered<T, TTier>(IReadOnlyList<T> candidates, Func<T, TTier> getTier,
            TrySelectReward<TTier> selectTier, TrySelectReward<T> selectCandidate, out T selected)
        {
            selected = default;
            if (candidates == null || candidates.Count == 0) return false;

            var tiers = new List<TTier>();
            var groups = new Dictionary<TTier, List<T>>();
            foreach (T candidate in candidates)
            {
                TTier tier = getTier(candidate);
                if (!groups.TryGetValue(tier, out var group))
                {
                    groups.Add(tier, group = new List<T>());
                    tiers.Add(tier);
                }

                group.Add(candidate);
            }

            return selectTier(tiers, out TTier tierSelected) &&
                   groups.TryGetValue(tierSelected, out var eligible) &&
                   selectCandidate(eligible, out selected);
        }

        /// <summary>Draws without replacement. Uniform pools consume the caller's integer draws.</summary>
        public static List<T> SelectDistinct<T>(IReadOnlyList<(T Item, double Weight)> candidates,
            int count, Random random)
        {
            var result = new List<T>();
            if (candidates == null || count <= 0 || random == null) return result;

            var remaining = new List<(T Item, double Weight)>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (candidate.Weight < 0 || !WeightedSelection.IsFinite(candidate.Weight)) return result;
                if (candidate.Weight > 0) remaining.Add(candidate);
            }

            while (result.Count < count && remaining.Count > 0)
            {
                double firstWeight = remaining[0].Weight;
                int index;
                if (remaining.All(candidate => candidate.Weight == firstWeight))
                    index = random.Next(remaining.Count);
                else if (!WeightedSelection.TrySelectIndex(remaining, candidate => candidate.Weight,
                             random.NextDouble(), out index))
                    break;

                result.Add(remaining[index].Item);
                remaining.RemoveAt(index);
            }

            return result;
        }

        /// <summary>Reweights candidates while preserving each tier's original total weight for this draw.</summary>
        public static bool TryRedistributeTierWeights<TTier>(IReadOnlyList<TTier> tiers, float[] weights,
            IReadOnlyList<float> multipliers)
        {
            if (tiers == null || weights == null || multipliers == null ||
                weights.Length != tiers.Count || weights.Length != multipliers.Count)
                return false;

            var totals = new Dictionary<TTier, (double Base, double Adjusted)>();
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = weights[i];
                float multiplier = multipliers[i];
                if (!WeightedSelection.IsFinite(weight) || weight < 0 ||
                    !WeightedSelection.IsFinite(multiplier) || multiplier <= 0)
                    return false;

                var tier = tiers[i];
                totals.TryGetValue(tier, out var total);
                totals[tier] = (total.Base + weight, total.Adjusted + (double)weight * multiplier);
            }

            var adjusted = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] == 0) continue;
                var total = totals[tiers[i]];
                if (total.Base == total.Adjusted && multipliers[i] == 1)
                {
                    adjusted[i] = weights[i];
                    continue;
                }

                double weight = (double)weights[i] * multipliers[i] / total.Adjusted * total.Base;
                if (weight > float.MaxValue) return false;
                adjusted[i] = (float)weight;
            }

            Array.Copy(adjusted, weights, weights.Length);
            return true;
        }
    }
}
