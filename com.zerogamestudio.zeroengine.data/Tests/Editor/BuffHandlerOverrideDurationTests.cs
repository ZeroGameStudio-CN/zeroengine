using NUnit.Framework;
using UnityEngine;
using ZeroEngine.BuffSystem;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Tests.Data
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BuffHandlerOverrideDurationTests
    {
        private BuffData _data;
        private StubBuffStatTarget _target;

        [SetUp]
        public void SetUp()
        {
            _data = ScriptableObject.CreateInstance<BuffData>();
            _data.Duration = 10f;
            _target = new StubBuffStatTarget();
        }

        [TearDown]
        public void TearDown()
        {
            if (_data != null)
            {
                Object.DestroyImmediate(_data);
            }
        }

        [Test]
        public void Constructor_WithoutOverride_UsesDataDuration()
        {
            var handler = new BuffHandler(_data, _target);
            Assert.That(handler.RemainingTime, Is.EqualTo(10f));
        }

        [Test]
        public void OverrideDuration_SetsRemainingTimeImmediately()
        {
            var handler = new BuffHandler(_data, _target);
            handler.OverrideDuration(3f);
            Assert.That(handler.RemainingTime, Is.EqualTo(3f));
        }

        [Test]
        public void OverrideDuration_CalledAgainWithDifferentValue_UpdatesRemainingTime()
        {
            var handler = new BuffHandler(_data, _target);
            handler.OverrideDuration(2f);
            handler.OverrideDuration(5f);
            Assert.That(handler.RemainingTime, Is.EqualTo(5f));
        }

        [Test]
        public void RefreshDuration_AfterOverride_UsesOverriddenValueNotDataDuration()
        {
            var handler = new BuffHandler(_data, _target);
            handler.OverrideDuration(4f);
            handler.AddStacks(1);
            handler.RemoveStacks(1);
            handler.AddStacks(1);
            handler.RefreshDuration();
            Assert.That(handler.RemainingTime, Is.EqualTo(4f));
        }

        [Test]
        public void RefreshDuration_WithoutOverride_StillUsesDataDuration()
        {
            var handler = new BuffHandler(_data, _target);
            handler.RefreshDuration();
            Assert.That(handler.RemainingTime, Is.EqualTo(10f));
        }

        [Test]
        public void RestoreState_IsOrthogonalToOverride_DoesNotThrow()
        {
            var handler = new BuffHandler(_data, _target);
            handler.OverrideDuration(6f);
            Assert.DoesNotThrow(() => handler.RestoreState(2f, 1));
            Assert.That(handler.RemainingTime, Is.EqualTo(2f));
        }

        private sealed class StubBuffStatTarget : IBuffStatTarget
        {
            public void AddModifier(StatId statId, StatModifier modifier) { }
            public void RemoveModifier(StatId statId, StatModifier modifier) { }
        }
    }
}
