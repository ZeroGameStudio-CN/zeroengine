using System;
using NUnit.Framework;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Data.Tests
{
    public class LegacyStatCompatibilityTests
    {
        [Test]
        public void Formula_KeepsLegacyEvaluationAtDirtyReadTime()
        {
            var formula = UnityEngine.ScriptableObject.CreateInstance<ZeroEngine.Formula.FormulaAsset>();
            try
            {
                var initial = typeof(ZeroEngine.Formula.FormulaAsset).GetField("initialValue",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(initial, Is.Not.Null);
                initial.SetValue(formula, 5f);
                var stat = new Stat(100);
                stat.AddModifier(new StatModifier(0, StatModType.Flat) { Formula = formula });
                Assert.That(stat.Value, Is.EqualTo(105));
                initial.SetValue(formula, 42f);
                Assert.That(stat.Value, Is.EqualTo(105), "Stable reads retain the old cached result.");
                stat.NotifyModifiersChanged();
                Assert.That(stat.Value, Is.EqualTo(142), "Legacy Data evaluates its formula when dirty.");
            }
            finally { UnityEngine.Object.DestroyImmediate(formula); }
        }

        [Serializable]
        private class LegacyStatFields
        {
            public float BaseValue, MinValue, MaxValue;
        }

        [Test]
        public void DiagnosticState_DoesNotChangeLegacyUnityJsonFields()
        {
            var stat = new Stat(12.5f, -10, 999);
            stat.AddModifier(new StatModifier(2, StatModType.Flat));
            _ = stat.Value;
            string legacy = UnityEngine.JsonUtility.ToJson(new LegacyStatFields { BaseValue = 12.5f, MinValue = -10, MaxValue = 999 });
            Assert.That(UnityEngine.JsonUtility.ToJson(stat), Is.EqualTo(legacy));
            var restored = UnityEngine.JsonUtility.FromJson<Stat>(legacy);
            Assert.That(restored.BaseValue, Is.EqualTo(12.5f));
            Assert.That(restored.MinValue, Is.EqualTo(-10));
            Assert.That(restored.MaxValue, Is.EqualTo(999));
        }

        [Test]
        public void DefaultFacade_KeepsEnumIdsGroupedMathAndRawMultiplier()
        {
            Assert.That((int)StatModType.Flat, Is.EqualTo(100));
            Assert.That((int)StatModType.PercentAdd, Is.EqualTo(200));
            Assert.That((int)StatModType.PercentMult, Is.EqualTo(300));
            var stat = new Stat(100);
            stat.AddModifier(new StatModifier(2, StatModType.PercentMult, 0));
            stat.AddModifier(new StatModifier(10, StatModType.Flat, 2));
            stat.AddModifier(new StatModifier(.5f, StatModType.PercentAdd, 3));
            Assert.That(stat.Value, Is.EqualTo(330));
        }

        [Test]
        public void Cache_StableReadsDoNotRecalculateAndExplicitMutationInvalidates()
        {
            var stat = new Stat(3.123456f);
            var modifier = new StatModifier(2, StatModType.Flat);
            stat.AddModifier(modifier);
            Assert.That(stat.Value, Is.EqualTo((float)Math.Round(3.123456f + 2f, 4)));
            var before = stat.GetCalculationVersion();
            for (int i = 0; i < 100; i++) _ = stat.Value;
            Assert.That(stat.GetCalculationVersion().CalculationCount, Is.EqualTo(before.CalculationCount));
            modifier.Value = 4;
            stat.NotifyModifiersChanged();
            Assert.That(stat.Value, Is.EqualTo((float)Math.Round(3.123456f + 4f, 4)));
            Assert.That(stat.GetCalculationVersion().CalculationCount, Is.EqualTo(before.CalculationCount + 1));
        }

        [Test]
        public void SourceRemoval_KeepsReferenceTokensAndSurvivorOrder()
        {
            var stat = new Stat(10); var own = new object(); var other = new object();
            var first = new StatModifier(1, StatModType.Flat, 0, other);
            var removed = new StatModifier(2, StatModType.Flat, 1, own);
            var last = new StatModifier(3, StatModType.Flat, 2, other);
            stat.AddModifier(first); stat.AddModifier(removed); stat.AddModifier(last);
            stat.RemoveAllModifiersFromSource(own);
            Assert.That(stat.GetModifiers(), Is.EqualTo(new[] { first, last }));
            Assert.That(stat.Value, Is.EqualTo(14));
            Assert.That(stat.RemoveModifier(new StatModifier(1, StatModType.Flat, 0, other)), Is.False);
        }

        [Test]
        public void CurrentFacade_KeepsPositiveDeltaRatioAndNotificationConditions()
        {
            var flat = new CurrentStat(100, 50); int notifications = 0, zero = 0;
            flat.OnValueChange += (_, _) => notifications++;
            flat.OnCurrentValueZero += () => zero++;
            var modifier = new StatModifier(37, StatModType.Flat);
            flat.AddModifier(modifier, new object());
            Assert.That(flat.CurrentValue, Is.EqualTo(87));
            Assert.That(flat.RemoveModifier(modifier), Is.True);
            Assert.That(flat.CurrentValue, Is.EqualTo(87));
            flat.SetCurrent(87); Assert.That(notifications, Is.EqualTo(2));
            flat.SetCurrent(0); Assert.That(zero, Is.EqualTo(1));
            flat.SetCurrent(0); Assert.That(notifications, Is.EqualTo(3));
            var ratio = new CurrentStat(100, 50);
            ratio.AddModifier(new StatModifier(37, StatModType.Flat), new object(), IncreaseType.Percent);
            Assert.That(ratio.CurrentValue, Is.EqualTo(68.5f));
        }
    }
}
