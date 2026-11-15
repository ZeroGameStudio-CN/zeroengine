using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.BuffSystem.Runtime;

namespace ZeroEngine.Tests.Data
{
    public sealed class BuffChargeStateTests
    {
        [Test]
        public void SpentChargeSurvivesSnapshotAndReinstallationWithoutRefill()
        {
            var state = new BuffChargeState();
            Assert.That(state.GrantOnce("stable-source", 1, 1), Is.EqualTo(1));
            Assert.That(state.Stacks.RemoveStacks(null, 1), Is.EqualTo(1));
            var restored = new BuffChargeState();
            restored.Restore(state.Capture());
            Assert.That(restored.Initialized, Is.True);
            Assert.That(restored.GrantOnce("stable-source", 1, 1), Is.Zero);
            Assert.That(restored.Stacks.TotalStacks, Is.Zero);
            restored.Reset();
            Assert.That(restored.GrantOnce("stable-source", 1, 1), Is.EqualTo(1));
        }

        [Test]
        public void UnspentSnapshotDoesNotBorrowMutableMapsAndKeepsSourcesIndependent()
        {
            var state = new BuffChargeState();
            state.GrantOnce("one", 2, 3);
            state.Stacks.AddStacks("two", 1, 3, false);
            var snapshot = state.Capture();
            snapshot.Stacks.SourceStacks["one"] = 1;
            Assert.That(state.Stacks.TotalStacks, Is.EqualTo(3));
            state.Restore(snapshot);
            snapshot.Stacks.SourceStacks.Clear();
            Assert.That(state.Stacks.RemoveStacks("one", 1), Is.EqualTo(1));
            Assert.That(state.Stacks.SourceStacks["two"], Is.EqualTo(1));
        }

        [Test]
        public void InvalidRestoreCannotChangeTheActiveCharge()
        {
            var state = new BuffChargeState();
            state.GrantOnce("one", 1, 1);
            var invalid = state.Capture();
            invalid.Stacks.SourceStacks["one"] = -1;
            Assert.Throws<ArgumentException>(() => state.Restore(invalid));
            Assert.That(state.Stacks.TotalStacks, Is.EqualTo(1));
            Assert.That(state.Initialized, Is.True);
        }

        [Test]
        public void GrantIsPublishedBeforeAnyObserverCanReenter()
        {
            var state = new BuffChargeState();
            int nested = -1;
            var observer = new Observer(() => nested = state.GrantOnce("one", 1, 1));
            Assert.That(state.GrantOnce("one", 1, 1, observer), Is.EqualTo(1));
            Assert.That(nested, Is.Zero);
            Assert.That(state.Stacks.TotalStacks, Is.EqualTo(1));
        }

        private sealed class Observer : IBuffStackObserver
        {
            private readonly Action before;
            public Observer(Action before) => this.before = before;
            public bool IsActive => true;
            public void BeforeAddStack() => before();
            public void AfterAddStack() { }
            public void AfterRemoveStack() { }
        }
    }
}
