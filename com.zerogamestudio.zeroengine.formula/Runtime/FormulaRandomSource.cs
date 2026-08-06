using System;

namespace ZeroEngine.Formula
{
    public interface IFormulaRandomSource
    {
        int NextIntInclusive(int minInclusive, int maxInclusive);
    }

    public sealed class SystemFormulaRandomSource : IFormulaRandomSource
    {
        private readonly Random random;

        public SystemFormulaRandomSource(Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int NextIntInclusive(int minInclusive, int maxInclusive)
        {
            if (minInclusive > maxInclusive)
                throw new ArgumentOutOfRangeException(nameof(minInclusive), "Minimum must not exceed maximum.");

            if (maxInclusive == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxInclusive), "Maximum must be less than Int32.MaxValue.");

            return random.Next(minInclusive, maxInclusive + 1);
        }
    }
}
