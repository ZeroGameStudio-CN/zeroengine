using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Builds;

namespace ZeroEngine.Gameplay.Tests.Editor
{
    public class BuildRewardSelectionTests
    {
        [Test]
        public void CatalogReturnsStableIdForLegacySpelling()
        {
            var catalog = new BuildTagCatalog(new[] { "shield", " Shield ", " " });
            Assert.That(catalog.Ids, Is.EquivalentTo(new[] { "shield" }));
            Assert.That(catalog.TryResolve(" SHIELD ", out var id), Is.True);
            Assert.That(id, Is.EqualTo("shield"));
            Assert.That(catalog.TryResolve("burn", out _), Is.False);
        }

        [Test]
        public void CountsMatchingSourcesOnceAndCapsBonus()
        {
            var build = new BuildTagSnapshot(new[]
            {
                new BuildTagSource("relic:a", new[] { " Shield ", "charged" }),
                new BuildTagSource("relic:a", new[] { "shield" }),
                new BuildTagSource("forge:b", new[] { "charged" }),
                new BuildTagSource("weapon:c", new[] { "charged" })
            });

            Assert.That(build.GetMultiplier(new[] { "shield", "SHIELD" }), Is.EqualTo(1.5f));
            Assert.That(build.GetMultiplier(new[] { "shield", "charged" }), Is.EqualTo(2f));
            Assert.That(build.GetMultiplier(new[] { "unrelated" }), Is.EqualTo(1f));
            Assert.That(build.GetMultiplier(new[] { "charged" }, 0.25f, 1.4f), Is.EqualTo(1.4f));
        }

        [Test]
        public void TierDrawOnlyOffersCandidatesFromTheChosenTier()
        {
            var candidates = new[] { (Tier: 1, Id: "low"), (Tier: 2, Id: "high"), (Tier: 2, Id: "high-match") };

            bool found = RewardSelection.TrySelectTiered(candidates, item => item.Tier,
                (IReadOnlyList<int> tiers, out int chosen) =>
                {
                    Assert.That(tiers, Is.EqualTo(new[] { 1, 2 }));
                    chosen = 2;
                    return true;
                },
                (IReadOnlyList<(int Tier, string Id)> eligible, out (int Tier, string Id) chosen) =>
                {
                    Assert.That(eligible, Is.EqualTo(new[] { candidates[1], candidates[2] }));
                    chosen = eligible[1];
                    return true;
                }, out var result);

            Assert.That(found, Is.True);
            Assert.That(result.Id, Is.EqualTo("high-match"));
        }

        [Test]
        public void DifferentTiersKeepTheirTotalWeight()
        {
            float[] weights = { 3, 3, 2, 2 };
            Assert.That(RewardSelection.TryRedistributeTierWeights(new[] { 1, 1, 2, 2 }, weights,
                new[] { 1f, 2f, 1f, 1f }), Is.True);
            Assert.That(weights, Is.EqualTo(new[] { 2f, 4f, 2f, 2f }).Within(0.0001f));
        }

        [Test]
        public void OverflowingTierWeightsLeaveInputIntact()
        {
            float[] weights = { float.MaxValue, float.MaxValue };
            Assert.That(RewardSelection.TryRedistributeTierWeights(new[] { 1, 1 }, weights,
                new[] { 2f, 1f }), Is.False);
            Assert.That(weights, Is.EqualTo(new[] { float.MaxValue, float.MaxValue }));
        }

        [Test]
        public void DistinctDrawKeepsUniformRandomWhenOnlyEqualWeightsRemain()
        {
            var random = new FixedRandom();
            var selected = RewardSelection.SelectDistinct(new[] { ("off", 1d), ("match", 1.5d) }, 2, random);

            Assert.That(selected, Is.EqualTo(new[] { "match", "off" }));
            Assert.That(random.WeightedDraws, Is.EqualTo(1));
            Assert.That(random.UniformDraws, Is.EqualTo(1));
        }

        private sealed class FixedRandom : Random
        {
            public int WeightedDraws { get; private set; }
            public int UniformDraws { get; private set; }

            public override double NextDouble()
            {
                WeightedDraws++;
                return 0.5d;
            }

            public override int Next(int maxValue)
            {
                UniformDraws++;
                return 0;
            }
        }
    }
}
