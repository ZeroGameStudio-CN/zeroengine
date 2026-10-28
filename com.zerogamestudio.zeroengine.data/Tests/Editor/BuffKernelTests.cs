using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.BuffSystem.Runtime;

namespace ZeroEngine.Data.Tests
{
    // Core contracts: source ownership, callback ordering, detached recovery and injected clocks.
    public class BuffKernelTests
    {
        private sealed class Observer : IBuffStackObserver
        {
            public bool IsActive { get; set; } = true;
            public Action Before, After, Removed;
            public void BeforeAddStack() => Before?.Invoke();
            public void AfterAddStack() => After?.Invoke();
            public void AfterRemoveStack() => Removed?.Invoke();
        }

        [Test]
        public void Add_ClampsBatchAndPublishesBeforeAndAfterEachLayer()
        {
            var state = new BuffStackState();
            var counts = new List<int>();
            var observer = new Observer { Before = () => counts.Add(state.TotalStacks), After = () => counts.Add(state.TotalStacks) };
            Assert.That(state.AddStacks("source", 983, 3, false, observer), Is.EqualTo(3));
            Assert.That(counts, Is.EqualTo(new[] { 0, 1, 1, 2, 2, 3 }));
            Assert.That(state.SourceStacks["source"], Is.EqualTo(3));
            Assert.That(state.AddStacks("other", 1, 3, false, observer), Is.Zero);
            Assert.That(counts.Count, Is.EqualTo(6));
        }

        [Test]
        public void Add_StopsWhenBeforeCallbackReleasesItsHost()
        {
            var state = new BuffStackState();
            var observer = new Observer();
            observer.Before = () => observer.IsActive = false;
            Assert.That(state.AddStacks("source", 3, 3, false, observer), Is.Zero);
            Assert.That(state.TotalStacks, Is.Zero);
        }

        [Test]
        public void Add_ReentrantGrantsRetainCurrentPerLayerOrdering()
        {
            var state = new BuffStackState();
            int before = 0;
            var observer = new Observer { Before = () => { if (++before == 1) state.AddStacks("nested", 1, 10, false); } };
            Assert.That(state.AddStacks("outer", 2, 10, true, observer), Is.EqualTo(2));
            Assert.That(state.SourceStacks["nested"], Is.EqualTo(1));
            Assert.That(state.SourceLifetimeStacks["outer"], Is.EqualTo(2));
        }

        [Test]
        public void Remove_ConsumesScopedSubsetBeforeTheCallback()
        {
            var state = new BuffStackState();
            state.AddStacks("same", 2, 10, false);
            state.AddStacks("same", 2, 10, true);
            var snapshots = new List<(int Total, int Scoped)>();
            var observer = new Observer { Removed = () => snapshots.Add((state.TotalStacks, state.SourceLifetimeStacks.TryGetValue("same", out int n) ? n : 0)) };
            Assert.That(state.RemoveStacks("same", 3, observer), Is.EqualTo(3));
            Assert.That(snapshots, Is.EqualTo(new[] { (3, 1), (2, 0), (1, 0) }));
            Assert.That(state.SourceStacks["same"], Is.EqualTo(1));
        }

        [Test]
        public void Remove_ReentrantReplacementIsReadAgainForTheNextLayer()
        {
            var state = new BuffStackState();
            state.AddStacks("first", 1, 10, false);
            var observer = new Observer { Removed = () => state.AddStacks("reward", 1, 10, false) };
            Assert.That(state.RemoveStacks(null, 2, observer), Is.EqualTo(2));
            Assert.That(state.SourceStacks.ContainsKey("first"), Is.False);
            Assert.That(state.SourceStacks["reward"], Is.EqualTo(1));
        }

        [Test]
        public void Snapshot_IsIndependentAndRestoreDoesNotBorrowInputMaps()
        {
            var state = new BuffStackState();
            state.AddStacks("source", 3, 10, true);
            var snapshot = state.Capture();
            state.RemoveStacks("source", 1);
            Assert.That(snapshot.SourceStacks["source"], Is.EqualTo(3));
            var restored = new BuffStackState();
            restored.Restore(snapshot.SourceStacks, snapshot.SourceLifetimeStacks);
            snapshot.SourceStacks.Clear();
            snapshot.SourceLifetimeStacks.Clear();
            Assert.That(restored.TotalStacks, Is.EqualTo(3));
            Assert.That(restored.SourceLifetimeStacks["source"], Is.EqualTo(3));
        }

        [Test]
        public void Restore_InvalidSubsetDoesNotReplaceCurrentState()
        {
            var state = new BuffStackState();
            state.AddStacks("existing", 1, 5, false);
            Assert.Throws<ArgumentException>(() => state.Restore(new Dictionary<string, int> { ["x"] = 1 }, new Dictionary<string, int> { ["x"] = 2 }));
            Assert.That(state.SourceStacks["existing"], Is.EqualTo(1));
        }

        [Test]
        public void Restore_AbsentLifetimeMetadataKeepsOrdinaryLayers()
        {
            var state = new BuffStackState();
            state.Restore(new Dictionary<string, int> { ["old"] = 2 }, null);
            Assert.That(state.TotalStacks, Is.EqualTo(2));
            Assert.That(state.SourceLifetimeStacks, Is.Empty);
        }

        [Test]
        public void AggregateAdapter_RetainsSignedArithmeticAndZeroRemoval()
        {
            var state = new BuffStackState();
            state.SetSourceStackCount(string.Empty, -2);
            Assert.That(state.TotalStacks, Is.EqualTo(-2));
            state.SetSourceStackCount(string.Empty, 0);
            Assert.That(state.SourceStacks, Is.Empty);
        }

        [Test]
        public void Sources_DetachedCohortsKeepTheirOwnSlotsWithoutReplacingLiveLookup()
        {
            var group = new BuffSourceGroup<string, object>();
            var live = new object(); var detached = new object();
            group.Add("player", live);
            group.AddRestored("player", detached, false);
            Assert.That(group.Count, Is.EqualTo(2));
            Assert.That(group["player"], Is.SameAs(live));
            Assert.That(group.Remove("player", detached), Is.True);
            Assert.That(group["player"], Is.SameAs(live));
        }

        [Test]
        public void Sources_UnknownRestoredIdentityCanBeRemovedWithoutALiveLookup()
        {
            var group = new BuffSourceGroup<string, object>();
            var detached = new object();
            group.AddRestored(null, detached, false);
            Assert.That(group.Remove(null, detached), Is.True);
            Assert.That(group.Count, Is.Zero);
        }

        [Test]
        public void Sources_RemovedSlotsAreReusedInExistingLifoOrder()
        {
            var group = new BuffSourceGroup<string, object>();
            var a = new object(); var b = new object(); var c = new object(); var d = new object(); var e = new object();
            group.Add("a", a); group.Add("b", b); group.Add("c", c);
            group.Remove("a", a); group.Remove("c", c);
            group.Add("d", d); group.Add("e", e);
            Assert.That(group.Values, Is.EqualTo(new[] { e, b, d }));
        }

        [Test]
        public void Sources_ReplacingLiveHandlerRetainsSlotAndCount()
        {
            var group = new BuffSourceGroup<string, object>();
            var original = new object(); var replacement = new object();
            group.Add("a", original); group["a"] = replacement;
            Assert.That(group.Count, Is.EqualTo(1));
            Assert.That(group.Contains(original), Is.False);
            Assert.That(group.Values, Is.EqualTo(new[] { replacement }));
        }

        [Test]
        public void Clock_GlobalExpiryCanAdvanceWithoutAdvancingFrozenOwnerTicks()
        {
            float anchor = 10;
            Assert.That(BuffClock.SelectDurationDelta(false, 0, .5f), Is.Zero);
            Assert.That(BuffClock.AdvanceDuration(2, BuffClock.SelectDurationDelta(true, 0, .5f)), Is.EqualTo(1.5f));
            Assert.That(BuffClock.TryTickAt(10, ref anchor, 1), Is.False);
            Assert.That(BuffClock.SelectDurationDelta(true, 0, 0), Is.Zero);
        }

        [Test]
        public void Clock_AbsoluteTicksRetainAnchorAndDropOvershoot()
        {
            float anchor = 0;
            Assert.That(BuffClock.TryTickAt(5, ref anchor, 1), Is.True);
            Assert.That(anchor, Is.EqualTo(5));
            Assert.That(BuffClock.TryTickAt(5.5f, ref anchor, 1), Is.False);
            Assert.That(BuffClock.TryTickAt(6, ref anchor, 1), Is.True);
        }

        [Test]
        public void Clock_LegacyDeltaTicksKeepRemainderUntilDueThenDropOvershoot()
        {
            float timer = 0;
            Assert.That(BuffClock.TryAdvanceTick(.6f, ref timer, 1), Is.False);
            Assert.That(BuffClock.TryAdvanceTick(2, ref timer, 1), Is.True);
            Assert.That(timer, Is.Zero);
            Assert.That(BuffClock.TryAdvanceTick(.5f, ref timer, 1), Is.False);
            Assert.That(BuffClock.TryAdvanceTick(1, ref timer, float.NaN), Is.False);
            Assert.That(timer, Is.EqualTo(.5f));
        }

        [Test]
        public void Lifecycle_ReusingAnActivationInvalidatesOldCallbacks()
        {
            var lifecycle = new BuffLifecycle();
            lifecycle.AdvanceGeneration(); lifecycle.Activate(); int old = lifecycle.Version;
            Assert.That(lifecycle.IsCurrent(old), Is.True);
            lifecycle.Release(); Assert.That(lifecycle.IsCurrent(old), Is.False);
            lifecycle.AdvanceGeneration(); lifecycle.Activate();
            Assert.That(lifecycle.IsCurrent(old), Is.False);
            Assert.That(lifecycle.IsCurrent(lifecycle.Version), Is.True);
        }
    }
}
