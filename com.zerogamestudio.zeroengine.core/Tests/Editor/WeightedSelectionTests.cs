using NUnit.Framework;

namespace ZeroEngine.Tests
{
    public sealed class WeightedSelectionTests
    {
        [TestCase(0d, 1)]
        [TestCase(0.249d, 1)]
        [TestCase(0.25d, 3)]
        [TestCase(0.999d, 3)]
        public void Intervals_ExcludeZeroWeights(double unit, int expected)
        {
            Assert.IsTrue(WeightedSelection.TrySelectIndex(new[] { 0d, 1, 0, 3 }, x => x, unit, out int index));
            Assert.AreEqual(expected, index);
        }

        [Test]
        public void InvalidPools_FailWithoutFallback()
        {
            foreach (var weights in new[] { new double[0], new[] { 0d, 0 }, new[] { -1d, 2 }, new[] { double.NaN }, new[] { double.PositiveInfinity } })
                Assert.IsFalse(WeightedSelection.TrySelectIndex(weights, x => x, 0, out _));
            Assert.IsFalse(WeightedSelection.TrySelectIndex(new[] { 1d }, x => x, 1, out _));
        }

        [Test]
        public void ExtremeFiniteWeights_DoNotOverflow()
        {
            Assert.IsTrue(WeightedSelection.TrySelectIndex(new[] { double.MaxValue, double.MaxValue }, x => x, .75, out int index));
            Assert.AreEqual(1, index);
        }

        [Test]
        public void ExtremeLuckAndScale_KeepMiddleRankNeutralAndAllRanksFinite()
        {
            for (int rank = 0; rank < 5; rank++)
                Assert.IsTrue(WeightedSelection.IsFinite(WeightedSelection.LuckMultiplier(rank, 5,
                    double.MaxValue, double.Epsilon, double.MaxValue)));
            Assert.AreEqual(1, WeightedSelection.LuckMultiplier(2, 5, double.MaxValue, double.Epsilon, double.MaxValue));
        }

        [TestCase(5)]
        [TestCase(6)]
        public void Luck_IsSymmetricNeutralAndFinite(int ranks)
        {
            for (int rank = 0; rank < ranks; rank++)
            {
                Assert.AreEqual(1, WeightedSelection.LuckMultiplier(rank, ranks, 0, 300, 1.5));
                Assert.AreEqual(WeightedSelection.LuckMultiplier(rank, ranks, 300, 300, 1.5),
                    WeightedSelection.LuckMultiplier(ranks - rank - 1, ranks, -300, 300, 1.5), 1e-12);
                Assert.IsTrue(WeightedSelection.IsFinite(WeightedSelection.LuckMultiplier(rank, ranks, float.MaxValue, 300, 1.5)));
            }
            Assert.Greater(WeightedSelection.LuckMultiplier(ranks - 1, ranks, 300, 300, 1.5), 1);
            Assert.Less(WeightedSelection.LuckMultiplier(0, ranks, 300, 300, 1.5), 1);
        }
    }
}
