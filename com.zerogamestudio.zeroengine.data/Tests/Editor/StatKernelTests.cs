using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.StatSystem.Core;

namespace ZeroEngine.Data.Tests
{
    public class StatKernelTests
    {
        private sealed class Modifier
        {
            public ModifierKind Kind;
            public int Order;
            public float Value;
            public string Source;
        }
        private struct Adapter : IStatModifierAdapter<Modifier, string>
        {
            public ModifierKind Kind(Modifier m) => m.Kind;
            public int Order(Modifier m) => m.Order;
            public int TypeId(Modifier m) => (int)m.Kind;
            public float Value(Modifier m) => m.Value;
            public string Source(Modifier m) => m.Source;
            public bool Matches(Modifier a, Modifier b, string source) => a != null && b != null && a.Source == source &&
                (ReferenceEquals(a, b) || a.Kind == b.Kind && a.Order == b.Order && Math.Abs(a.Value - b.Value) < .00001f);
        }
        private static int Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

        [TestCase(19)]
        [TestCase(499)]
        [TestCase(4999)]
        public void Ordered_RepeatedPercentKeepsFloat32Sequence(int count)
        {
            var m = new Modifier { Kind = ModifierKind.AdditivePercent, Value = .05f };
            var input = new RepeatedModifiers<Modifier>(new[] { m }, count);
            float sum = 0;
            for (int i = 0; i < count; i++) sum += .05f;
            float expected = (float)Math.Ceiling(100f * (1f + sum));
            Assert.That(Bits(StatKernel<Modifier, string, Adapter>.EvaluateOrdered(100, input, true, true)), Is.EqualTo(Bits(expected)));
            if (count == 19) Assert.That(expected, Is.EqualTo(196));
            Assert.That(input.TemplateCount, Is.EqualTo(1));
            Assert.That(input.Count, Is.EqualTo(count));
        }

        [Test]
        public void Policies_KeepMixedOrderAndMultiplicationDistinct()
        {
            var input = new[] {
                new Modifier { Kind = ModifierKind.MultiplicativePercent, Value = 2 },
                new Modifier { Kind = ModifierKind.Flat, Value = 10 },
                new Modifier { Kind = ModifierKind.AdditivePercent, Value = .5f }
            };
            Assert.That(StatKernel<Modifier, string, Adapter>.EvaluateOrdered(100, input, false, true), Is.EqualTo(465));
            Assert.That(StatKernel<Modifier, string, Adapter>.EvaluateGrouped(100, input, float.MinValue, float.MaxValue), Is.EqualTo(330));
        }

        [Test]
        public void Ordered_RandomMixedSequencesMatchFrozenArithmeticBits()
        {
            var random = new Random(417);
            for (int trial = 0; trial < 200; trial++)
            {
                var list = new List<Modifier>();
                for (int i = 0; i < 47; i++) list.Add(new Modifier { Kind = (ModifierKind)random.Next(4), Value = (float)(random.NextDouble() - .5) });
                float value = 123.456f, sum = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    if (m.Kind == ModifierKind.Flat) value += m.Value;
                    else if (m.Kind == ModifierKind.MultiplicativePercent) value *= 1 + m.Value;
                    else if (m.Kind == ModifierKind.AdditivePercent)
                    {
                        sum += m.Value;
                        if (i + 1 == list.Count || list[i + 1].Kind != ModifierKind.AdditivePercent) { value *= 1 + sum; sum = 0; }
                    }
                }
                Assert.That(Bits(StatKernel<Modifier, string, Adapter>.EvaluateOrdered(123.456f, list, false, true)), Is.EqualTo(Bits(value)));
            }
        }

        [Test]
        public void Sort_EqualOrderDifferentReferencesKeepsLegacyUnstableOrder()
        {
            var list = new List<Modifier>();
            for (int i = 0; i < 70; i++) list.Add(new Modifier { Order = i % 3, Value = i });
            var expected = new List<Modifier>(list);
            Comparison<Modifier> compare = (a, b) => a.Order.CompareTo(b.Order);
            expected.Sort(compare);
            StatKernel<Modifier, string, Adapter>.Sort(list, compare);
            Assert.That(list, Is.EqualTo(expected));
        }

        [Test]
        public void RemoveBatch_OverlappingValuesKeepGreedyReferenceIdentity()
        {
            var a = new Modifier { Source = "s", Value = 1 };
            var b = new Modifier { Source = "s", Value = 1.000005f };
            var c = new Modifier { Source = "s", Value = 1.000012f };
            var list = new List<Modifier> { a, b, c };
            int count = StatKernel<Modifier, string, Adapter>.RemoveBatch(list, new[] { b, a }, "s", out bool fallback);
            Assert.That(count, Is.EqualTo(2)); Assert.That(fallback, Is.True);
            Assert.That(list, Is.EqualTo(new[] { c }));
        }

        [Test]
        public void RemoveBatch_DisjointTemplatesCompactStablyWithoutChangingOtherSources()
        {
            var a = new Modifier { Kind = ModifierKind.Flat, Source = "s" };
            var b = new Modifier { Kind = ModifierKind.AdditivePercent, Source = "s" };
            var keep = new Modifier { Source = "other" };
            var list = new List<Modifier> { a, keep, b, a, b, keep, a };
            int count = StatKernel<Modifier, string, Adapter>.RemoveBatch(list, new RepeatedModifiers<Modifier>(new[] { a, b }, 2), "s", out bool fallback);
            Assert.That(count, Is.EqualTo(4)); Assert.That(fallback, Is.False);
            Assert.That(list, Is.EqualTo(new[] { keep, keep, a }));
        }

        [Test]
        public void NoMatch_DoesNotInvalidateActiveEnumerators()
        {
            var m = new Modifier { Source = "s" };
            var list = new List<Modifier> { m, m };
            var iterator = list.GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(StatKernel<Modifier, string, Adapter>.RemoveBatch(list, new[] { m }, "absent", out _), Is.Zero);
            Assert.That(StatKernel<Modifier, string, Adapter>.RemoveSource(list, "absent", StringComparer.Ordinal), Is.Zero);
            Assert.That(iterator.MoveNext(), Is.True);
        }

        [Test]
        public void CurrentValuePolicies_KeepRatioCeilingAndPositiveDeltaSeparate()
        {
            Assert.That(StatCurrentValue.PreserveRatioCeiling(137, .5f), Is.EqualTo(69));
            Assert.That(StatCurrentValue.AdjustLegacyMaximum(50, 100, 137, false), Is.EqualTo(87));
            Assert.That(StatCurrentValue.AdjustLegacyMaximum(50, 100, 137, true), Is.EqualTo(68.5f));
            Assert.That(StatCurrentValue.AdjustLegacyMaximum(50, 100, 30, false), Is.EqualTo(30));
            Assert.That(float.IsNaN(StatCurrentValue.Clamp(float.NaN, 0, 100)), Is.True);
        }

        [Test]
        public void Versions_StableReadsDoNotAdvanceCalculationOrInputVersions()
        {
            var version = new StatCalculationVersion();
            version.Invalidate(); version.RecordCalculation();
            Assert.That(version.InputVersion, Is.EqualTo(1));
            Assert.That(version.CalculatedVersion, Is.EqualTo(1));
            Assert.That(version.CalculationCount, Is.EqualTo(1));
            version.Invalidate();
            Assert.That(version.CalculatedVersion, Is.EqualTo(1));
            Assert.That(version.InputVersion, Is.EqualTo(2));
        }
    }
}
