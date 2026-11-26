using System;
using NUnit.Framework;
using ZeroEngine.AI.RunDecisions;

namespace ZeroEngine.AI.Tests
{
    public sealed class RunDecisionStrategyTests
    {
        [Test]
        public void ReadsCurrentWeightsAndReturnsOnlyAnOfferedChoice()
        {
            IRunDecisionStrategy strategy = new WeightedRunDecisionStrategy(new FixedRandom(0.45));
            int[] costs = { 100, 200 };
            int balance = 150;
            int calls = 0;
            float Weight(int cost)
            {
                calls++;
                return balance >= cost ? cost : 0;
            }

            Assert.That(strategy.TryChoose(costs, Weight, out int before), Is.True);
            Assert.That(before, Is.EqualTo(100));
            balance = 250;
            Assert.That(strategy.TryChoose(costs, Weight, out int after), Is.True);
            Assert.That(after, Is.EqualTo(200));
            Assert.That(calls, Is.EqualTo(4));
        }

        [Test]
        public void EmptyOrInvalidOffersNeverPickEvenWithCompatibilityEnabled()
        {
            var random = new FixedRandom(0);
            var strategy = new WeightedRunDecisionStrategy(random, preserveFirstOnZero: true);

            Assert.That(strategy.TryChoose(Array.Empty<int>(), _ => 1f, out _), Is.False);
            Assert.That(strategy.TryChoose(new[] { 0, 1 }, value => value == 0 ? 0 : float.NaN, out _), Is.False);
            Assert.That(strategy.TryChoose(new string[] { "door", null }, _ => 1f, out _), Is.False);
            Assert.That(random.Draws, Is.EqualTo(3));
            var invalidDraw = new WeightedRunDecisionStrategy(new FixedRandom(double.NaN), preserveFirstOnZero: true);
            Assert.That(invalidDraw.TryChoose(new[] { 0 }, _ => 0f, out _), Is.False);
        }

        [Test]
        public void ZeroWeightAndZeroDrawPreserveExistingHostTiesOnlyWhenRequested()
        {
            var normal = new WeightedRunDecisionStrategy(new FixedRandom(0));
            var compatible = new WeightedRunDecisionStrategy(new FixedRandom(0), preserveFirstOnZero: true);
            int[] choices = { 0, 1 };

            Assert.That(normal.TryChoose(choices, _ => 0f, out _), Is.False);
            Assert.That(normal.TryChoose(choices, value => value, out int defaultChoice), Is.True);
            Assert.That(defaultChoice, Is.EqualTo(1));

            Assert.That(compatible.TryChoose(choices, _ => 0f, out int fallback), Is.True);
            Assert.That(fallback, Is.Zero);
            Assert.That(compatible.TryChoose(choices, value => value, out int zeroDraw), Is.True);
            Assert.That(zeroDraw, Is.Zero);
        }

        [Test]
        public void BinaryChoiceUsesOneDrawAndCanRetainMoney()
        {
            var random = new FixedRandom(0.75);
            IRunDecisionStrategy strategy = new WeightedRunDecisionStrategy(random);
            Assert.That(strategy.ChooseFirst(3, 1), Is.False);
            Assert.That(strategy.ChooseFirst(4, 1), Is.True);
            Assert.That(strategy.ChooseFirst(0, 1), Is.False);
            Assert.That(strategy.ChooseFirst(1, 0), Is.True);
            Assert.That(strategy.ChooseFirst(float.MaxValue, float.MaxValue), Is.False);
            Assert.That(random.Draws, Is.EqualTo(5));
        }

        [Test]
        public void SeededChoicesDoNotDrawFromAnotherRun()
        {
            var first = new WeightedRunDecisionStrategy(new Random(334));
            var second = new WeightedRunDecisionStrategy(new Random(334));
            int[] choices = { 1, 2, 3 };

            for (int index = 0; index < 32; index++)
            {
                Assert.That(first.TryChoose(choices, candidate => candidate, out int expected), Is.True);
                Assert.That(second.TryChoose(choices, candidate => candidate, out int actual), Is.True);
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(first.ChooseFirst(2, 1), Is.EqualTo(second.ChooseFirst(2, 1)));
            }
        }

        private sealed class FixedRandom : Random
        {
            private readonly double value;
            public int Draws { get; private set; }

            public FixedRandom(double value) => this.value = value;

            public override double NextDouble()
            {
                Draws++;
                return value;
            }
        }
    }
}
