using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Data
{
    [TestFixture]
    public sealed class StatsLifecycleTests
    {
        [TestCase(StatType.MaxHP, 100f, 50f)]
        [TestCase(StatType.MaxMP, 40f, 20f)]
        public void Clear_RetiredCurrentStat_DetachesValueAndZeroNotifications(
            StatType type, float maximum, float current)
        {
            var stats = new Stats();
            var retired = stats.GetOrCreateAndInit<CurrentStat>(type,
                stat => stat.InitCurrent(maximum, current));
            var maximumChanges = 0;
            var currentChanges = 0;
            var zeroTransitions = 0;
            retired.OnValueChanged += _ => maximumChanges++;
            retired.OnValueChange += (_, _) => currentChanges++;
            retired.OnCurrentValueZero += () => zeroTransitions++;
            retired.ForceRecalculate();
            retired.SetCurrent(0f);
            retired.SetCurrent(current);
            Assert.That(maximumChanges, Is.GreaterThan(0));
            Assert.That(currentChanges, Is.GreaterThan(0));
            Assert.That(zeroTransitions, Is.EqualTo(1));

            stats.Clear();
            stats.Clear();
            maximumChanges = currentChanges = zeroTransitions = 0;
            retired.BaseValue += 10f;
            retired.ForceRecalculate();
            retired.SetCurrent(0f);
            retired.IncreaseCurrent(5f);

            Assert.That(stats.GetStat(type), Is.Null);
            Assert.That(maximumChanges, Is.Zero);
            Assert.That(currentChanges, Is.Zero);
            Assert.That(zeroTransitions, Is.Zero);
        }

        [Test]
        public void ClearThenReinitialize_RetiredResourcesCannotNotifyReplacementContainer()
        {
            var stats = new Stats();
            var oldHp = stats.GetOrCreateAndInit<CurrentStat>(StatType.MaxHP,
                stat => stat.InitCurrent(100f, 50f));
            var oldMp = stats.GetOrCreateAndInit<CurrentStat>(StatType.MaxMP,
                stat => stat.InitCurrent(40f, 20f));
            var notifications = new List<string>();
            oldHp.OnValueChange += (_, _) => notifications.Add("retired-hp");
            oldHp.OnCurrentValueZero += () => notifications.Add("retired-hp-zero");
            oldMp.OnValueChange += (_, _) => notifications.Add("retired-mp");
            oldMp.OnCurrentValueZero += () => notifications.Add("retired-mp-zero");

            stats.Clear();
            var hp = stats.GetOrCreateAndInit<CurrentStat>(StatType.MaxHP,
                stat => stat.InitCurrent(200f, 150f));
            var mp = stats.GetOrCreateAndInit<CurrentStat>(StatType.MaxMP,
                stat => stat.InitCurrent(60f, 30f));
            hp.OnValueChange += (_, _) => notifications.Add("hp");
            hp.OnCurrentValueZero += () => notifications.Add("hp-zero");
            mp.OnValueChange += (_, _) => notifications.Add("mp");
            mp.OnCurrentValueZero += () => notifications.Add("mp-zero");

            oldHp.SetCurrent(0f);
            oldMp.SetCurrent(0f);
            oldHp.AddModifier(new StatModifier(25f, StatModType.Flat), oldHp);
            oldMp.AddModifier(new StatModifier(10f, StatModType.Flat), oldMp);

            Assert.That(notifications, Is.Empty);
            Assert.That(stats.GetStat<CurrentStat>(StatType.MaxHP), Is.SameAs(hp).And.Not.SameAs(oldHp));
            Assert.That(stats.GetStat<CurrentStat>(StatType.MaxMP), Is.SameAs(mp).And.Not.SameAs(oldMp));
            Assert.That(hp.CurrentValue, Is.EqualTo(150f));
            Assert.That(hp.MaxValue, Is.EqualTo(200f));
            Assert.That(mp.CurrentValue, Is.EqualTo(30f));
            Assert.That(mp.MaxValue, Is.EqualTo(60f));

            hp.SetCurrent(0f);
            mp.SetCurrent(0f);

            Assert.That(notifications, Is.EqualTo(new[] { "hp", "hp-zero", "mp", "mp-zero" }));
        }
    }
}
