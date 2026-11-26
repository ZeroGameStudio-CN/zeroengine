using System;
using System.Collections.Generic;

namespace ZeroEngine.AI.RunDecisions
{
    /// <summary>Chooses from a game's current candidates. The game executes the choice.</summary>
    public interface IRunDecisionStrategy
    {
        bool TryChoose<T>(IReadOnlyList<T> candidates, Func<T, float> weight, out T choice);
        bool ChooseFirst(float firstWeight, float secondWeight);
    }

    /// <summary>Samples ordered candidates with a caller-owned random stream.</summary>
    public sealed class WeightedRunDecisionStrategy : IRunDecisionStrategy
    {
        private readonly Random random;
        private readonly bool preserveFirstOnZero;

        /// <param name="random">A random stream scoped to the current run or player.</param>
        /// <param name="preserveFirstOnZero">Use the first candidate at zero total weight or a zero draw for legacy lists.</param>
        public WeightedRunDecisionStrategy(Random random, bool preserveFirstOnZero = false)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.preserveFirstOnZero = preserveFirstOnZero;
        }

        /// <summary>Consumes one draw per attempt. Scores must be finite and nonnegative.</summary>
        public bool TryChoose<T>(IReadOnlyList<T> candidates, Func<T, float> weight, out T choice)
        {
            choice = default;
            double unit = random.NextDouble();
            if (candidates == null || candidates.Count == 0 || weight == null || !(unit >= 0 && unit < 1))
                return false;

            bool allZero = true;
            double ReadWeight(T candidate)
            {
                double value = candidate is null ? double.NaN : weight(candidate);
                allZero &= value == 0;
                return value;
            }

            if (WeightedSelection.TrySelectIndex(candidates, ReadWeight, unit, out int index))
            {
                choice = candidates[preserveFirstOnZero && unit == 0 ? 0 : index];
                return true;
            }

            if (!preserveFirstOnZero || !allZero)
                return false;
            choice = candidates[0];
            return true;
        }

        /// <summary>Chooses between two alternatives with one draw, such as purchasing or saving.</summary>
        public bool ChooseFirst(float firstWeight, float secondWeight)
        {
            float unit = (float)random.NextDouble();
            if (!WeightedSelection.IsFinite(firstWeight) || !WeightedSelection.IsFinite(secondWeight) ||
                firstWeight <= 0 || secondWeight < 0)
                return false;

            if (secondWeight == 0)
                return true;

            // Keep the float comparison used by hosts for ordinary weights. Widen only if their sum overflows.
            float total = firstWeight + secondWeight;
            return float.IsInfinity(total)
                ? unit * ((double)firstWeight + secondWeight) < firstWeight
                : unit * total < firstWeight;
        }
    }
}
