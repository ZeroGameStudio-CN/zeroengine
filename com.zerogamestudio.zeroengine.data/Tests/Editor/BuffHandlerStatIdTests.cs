using NUnit.Framework;
using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Data
{
    [TestFixture]
    public sealed class BuffHandlerStatIdTests
    {
        private sealed class RecordingStatTarget : IBuffStatTarget
        {
            public int AddCount;
            public int RemoveCount;
            public StatId LastAddedStatId;

            public void AddModifier(StatId statId, StatModifier modifier)
            {
                AddCount++;
                LastAddedStatId = statId;
            }

            public void RemoveModifier(StatId statId, StatModifier modifier)
            {
                RemoveCount++;
            }
        }

        [Test]
        public void AddStacks_WithStatIdModifierConfig_CallsTargetAddModifierWithStatId()
        {
            var buffData = ScriptableObject.CreateInstance<BuffData>();
            buffData.MaxStacks = 1;
            buffData.StatModifiers.Add(new BuffStatModifierConfig
            {
                StatId = new StatId("meridian.test_stat"),
                Value = 10f,
                ModType = StatModType.Flat
            });

            var target = new RecordingStatTarget();
            var handler = new BuffHandler(buffData, target);
            handler.AddStacks(1);

            Assert.That(target.AddCount, Is.EqualTo(1));
            Assert.That(target.LastAddedStatId, Is.EqualTo(new StatId("meridian.test_stat")));
            Object.DestroyImmediate(buffData);
        }

        [Test]
        public void RemoveStacks_ToZero_CallsTargetRemoveModifier()
        {
            var buffData = ScriptableObject.CreateInstance<BuffData>();
            buffData.MaxStacks = 1;
            buffData.StatModifiers.Add(new BuffStatModifierConfig
            {
                StatId = new StatId("meridian.test_stat"),
                Value = 10f,
                ModType = StatModType.Flat
            });

            var target = new RecordingStatTarget();
            var handler = new BuffHandler(buffData, target);
            handler.AddStacks(1);
            handler.RemoveStacks(1);

            Assert.That(target.RemoveCount, Is.EqualTo(1));
            Assert.That(handler.IsExpired, Is.True);
            Object.DestroyImmediate(buffData);
        }

        [Test]
        public void Tick_WithZeroDuration_NeverExpires()
        {
            var buffData = ScriptableObject.CreateInstance<BuffData>();
            buffData.Duration = 0f;
            var target = new RecordingStatTarget();
            var handler = new BuffHandler(buffData, target);
            handler.AddStacks(1);
            handler.Tick(999999f);

            Assert.That(handler.IsExpired, Is.False);
            Object.DestroyImmediate(buffData);
        }
    }
}
