using System;
using NUnit.Framework;

namespace ZeroEngine.FogOfWar.Tests
{
    // Core contracts: membership identity, synchronous callbacks, pool cleanup and warm evaluation.
    public sealed class VisibilityWorldTests
    {
        private sealed class Target : IFogVisibilityTarget
        {
            internal FogPosition Position;
            internal bool Valid = true, Visible = true;
            internal int Applies, Releases;
            internal Action OnPosition, OnApply, OnRelease;
            public bool TryGetPosition(out FogPosition position) { OnPosition?.Invoke(); position = Position; return Valid; }
            public void ApplyVisibility(bool visible) { Visible = visible; Applies++; OnApply?.Invoke(); }
            public void ReleaseVisibility() { Visible = true; Releases++; OnRelease?.Invoke(); }
        }
        private sealed class Query : IFogVisibilityQuery
        {
            internal Action Callback;
            public bool IsVisible(FogPosition position) { Callback?.Invoke(); return position.X >= 0; }
        }

        [Test]
        public void RegistrationIsIdempotentAndStaleOrForeignHandlesCannotRemoveReusedSlots()
        {
            using var world = new VisibilityWorld(1, 2);
            using var foreign = new VisibilityWorld();
            var first = new Target(); var next = new Target();
            var old = world.Register(first);
            Assert.That(world.Register(first), Is.EqualTo(old));
            Assert.That(world.Unregister(foreign.Register(next)), Is.False);
            Assert.That(world.Unregister(old), Is.True);
            var current = world.Register(next);
            Assert.That(world.Unregister(old), Is.False);
            Assert.That(world.Contains(current), Is.True);
            Assert.That(first.Releases, Is.EqualTo(1));
            Assert.That(world.Count, Is.EqualTo(1));
        }

        [Test]
        public void MovementAndRepeatedCallsObserveCurrentPositionsWithoutAFrameClock()
        {
            using var world = new VisibilityWorld(); var target = new Target(); var query = new Query();
            world.Register(target);
            world.Evaluate(query); Assert.That(target.Visible, Is.True);
            target.Position = new FogPosition(-1, 2);
            world.Evaluate(query); Assert.That(target.Visible, Is.False);
            target.Position = new FogPosition(1, 2);
            world.Evaluate(query); Assert.That(target.Visible, Is.True);
            Assert.That(target.Applies, Is.EqualTo(3));
        }

        [Test]
        public void CallbackReplacementAndNewTargetsAreEvaluatedInTheSameCall()
        {
            using var world = new VisibilityWorld(2, 3);
            var first = new Target(); var removed = new Target(); var replacement = new Target { Position = new FogPosition(-3, 0) };
            world.Register(first); var handle = world.Register(removed);
            first.OnApply = () => { world.Unregister(handle); world.Register(replacement); };
            Assert.That(world.Evaluate(new Query()), Is.EqualTo(2));
            Assert.That(removed.Applies, Is.Zero);
            Assert.That(removed.Releases, Is.EqualTo(1));
            Assert.That(replacement.Applies, Is.EqualTo(1));
            Assert.That(replacement.Visible, Is.False);
        }

        [TestCase(true), TestCase(false)]
        public void RemovalFromPositionOrQueryPreventsAStalePresentationWrite(bool fromPosition)
        {
            using var world = new VisibilityWorld(); var target = new Target(); var query = new Query();
            var handle = world.Register(target);
            if (fromPosition) target.OnPosition = () => world.Unregister(handle);
            else query.Callback = () => world.Unregister(handle);
            Assert.That(world.Evaluate(query), Is.Zero);
            Assert.That(target.Applies, Is.Zero);
            Assert.That(target.Releases, Is.EqualTo(1));
        }

        [Test]
        public void InvalidAndInactiveTargetsReleasePresentationAndCanBeReused()
        {
            using var world = new VisibilityWorld(); var query = new Query();
            var target = new Target { Position = new FogPosition(-1, 0) }; world.Register(target);
            world.Evaluate(query); Assert.That(target.Visible, Is.False);
            target.Valid = false; world.Evaluate(query); Assert.That(target.Visible, Is.True);
            target.Valid = true; target.Position = new FogPosition(float.NaN, 0);
            world.Evaluate(query); Assert.That(target.Releases, Is.EqualTo(2));
            target.Position = new FogPosition(-2, 0); world.Evaluate(query);
            Assert.That(target.Visible, Is.False);
        }

        [Test]
        public void CallbackChurnIsBoundedAndEvaluationRecoversAfterAnException()
        {
            using var world = new VisibilityWorld(1, 1); var query = new Query();
            FogTargetHandle handle = default;
            var target = new Target();
            target.OnApply = () => { world.Unregister(handle); handle = world.Register(target); };
            handle = world.Register(target);
            Assert.Throws<InvalidOperationException>(() => world.Evaluate(query));
            target.OnApply = null;
            handle = world.Register(target);
            Assert.That(world.Evaluate(query), Is.EqualTo(1));
            Assert.That(world.Contains(handle), Is.True);
        }

        [Test]
        public void DisposeReleasesEveryTargetEvenWhenOneCallbackFails()
        {
            var world = new VisibilityWorld();
            var first = new Target { OnRelease = () => throw new InvalidOperationException("Synthetic release failure") };
            var second = new Target();
            world.Register(first); world.Register(second);
            Assert.Throws<InvalidOperationException>(() => world.Dispose());
            Assert.That(second.Releases, Is.EqualTo(1));
            Assert.That(world.Count, Is.Zero);
            world.Dispose(); Assert.That(second.Releases, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => world.Register(new Target()));
        }

        [Test]
        public void DisposeDuringQueryPreventsAnyLaterApply()
        {
            var world = new VisibilityWorld(); var target = new Target(); world.Register(target);
            Assert.That(world.Evaluate(new Query { Callback = world.Dispose }), Is.Zero);
            Assert.That(target.Releases, Is.EqualTo(1)); Assert.That(target.Applies, Is.Zero);
        }

        [Test]
        public void NestedEvaluationIsRejectedWithoutCorruptingTheOuterRun()
        {
            using var world = new VisibilityWorld(); var query = new Query();
            var target = new Target { OnApply = () => Assert.Throws<InvalidOperationException>(() => world.Evaluate(query)) };
            world.Register(target); Assert.That(world.Evaluate(query), Is.EqualTo(1));
        }

        [Test]
        public void WarmEvaluationDoesNotAllocatePerTargetOrPerCall()
        {
            using var world = new VisibilityWorld(256); var query = new Query();
            for (int i = 0; i < 256; i++) world.Register(new Target { Position = new FogPosition(i - 128, i) });
            for (int i = 0; i < 8; i++) world.Evaluate(query);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) world.Evaluate(query);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
