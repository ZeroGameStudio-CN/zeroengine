using NUnit.Framework;
using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Data
{
    [TestFixture]
    public sealed class BuffHandlerRestoreStateTests
    {
        private sealed class RecordingStatTarget : IBuffStatTarget
        {
            public int AddCount;
            public void AddModifier(StatId statId, StatModifier modifier) => AddCount++;
            public void RemoveModifier(StatId statId, StatModifier modifier) { }
        }

        [Test]
        public void RestoreState_SetsRemainingTimeAndStacks()
        {
            var buffData = ScriptableObject.CreateInstance<BuffData>();
            buffData.Duration = 30f;
            buffData.MaxStacks = 3;
            buffData.StatModifiers.Add(new BuffStatModifierConfig
            {
                StatId = new StatId("meridian.test_stat"),
                Value = 5f,
                ModType = StatModType.Flat
            });

            var target = new RecordingStatTarget();
            var handler = new BuffHandler(buffData, target);
            handler.RestoreState(remainingTime: 12.5f, stacks: 2);

            Assert.That(handler.RemainingTime, Is.EqualTo(12.5f));
            Assert.That(handler.CurrentStacks, Is.EqualTo(2));
            Assert.That(target.AddCount, Is.EqualTo(2));
            Assert.That(handler.IsExpired, Is.False);
            Object.DestroyImmediate(buffData);
        }

        [Test]
        public void RestoreState_WithZeroStacks_LeavesNotExpired()
        {
            var buffData = ScriptableObject.CreateInstance<BuffData>();
            buffData.Duration = 0f;
            var target = new RecordingStatTarget();
            var handler = new BuffHandler(buffData, target);
            handler.RestoreState(remainingTime: 0f, stacks: 0);

            Assert.That(handler.CurrentStacks, Is.EqualTo(0));
            Assert.That(handler.IsExpired, Is.False);
            Object.DestroyImmediate(buffData);
        }
    }
}
