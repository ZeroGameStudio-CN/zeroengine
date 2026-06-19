using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Data.Editor.Tests
{
    public sealed class StatsContractTests
    {
        [Test]
        public void StatAppliesFlatAddPercentAndMultiplierInOrder()
        {
            var stat = new Stat(100f);
            stat.AddModifier(new StatModifier(25f, StatModType.Flat));
            stat.AddModifier(new StatModifier(0.2f, StatModType.PercentAdd));
            stat.AddModifier(new StatModifier(1.5f, StatModType.PercentMult));

            Assert.AreEqual(225f, stat.Value);
        }

        [Test]
        public void RemoveAllModifiersFromSourceInvalidatesCachedValue()
        {
            var source = new object();
            var stats = new Stats();
            var attack = new Stat(10f);
            attack.AddModifier(new StatModifier(5f, StatModType.Flat, (int)StatModType.Flat, source));
            stats.SetStat(StatType.Attack, attack);

            Assert.AreEqual(15f, attack.Value);

            stats.RemoveAllModifiersFromSource(source);

            Assert.AreEqual(10f, attack.Value);
        }

        [Test]
        public void LoadFromDataInvalidatesCachedValue()
        {
            var stats = new Stats();
            var attack = new Stat(10f);
            stats.SetStat(StatType.Attack, attack);

            Assert.AreEqual(10f, attack.Value);

            stats.LoadFromData(new Dictionary<StatType, float>
            {
                [StatType.Attack] = 42.5f
            });

            Assert.AreEqual(42.5f, attack.Value);
        }

        [Test]
        public void CurrentStatClampsCurrentValueToNewMaximumWhenModifierIsRemoved()
        {
            var source = new object();
            var health = new CurrentStat(100f, 100f, round: false);
            var bonus = new StatModifier(50f, StatModType.Flat, (int)StatModType.Flat, source);

            health.AddModifier(bonus, source);
            health.SetCurrent(150f);

            Assert.AreEqual(150f, health.CurrentValue);

            health.RemoveModifier(bonus);

            Assert.AreEqual(100f, health.CurrentValue);
        }
    }
}
