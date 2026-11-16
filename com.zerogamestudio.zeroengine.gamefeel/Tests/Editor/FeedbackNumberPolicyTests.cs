using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class FeedbackNumberPolicyTests
    {
        private sealed class Target { internal int Generation; }
        private sealed class Display { internal uint Version = 1; internal bool Active = true; }
        private static FeedbackNumberMerger<Target, int, Display, int> Numbers(int capacity = 32) => new(
            t => t.Generation, (t, generation) => t.Generation == generation,
            (d, version) => d.Active && d.Version == version, capacity);
        private static FeedbackNumberContext Context(FeedbackNumberMode mode = FeedbackNumberMode.AdaptiveStream,
            long group = 1, ulong owner = 1, ulong source = 1, bool important = false) => new(mode, owner, source, group, important);
        private static FeedbackNumberMerge<Display> Add(FeedbackNumberMerger<Target, int, Display, int> n,
            Target t, FeedbackNumberContext c, double time, double value = 10, int style = 0, Display display = null, double window = .15)
            => n.Add(t, style, c, display ?? new Display(), display?.Version ?? 1, value, time, window, false);

        [Test]
        public void IndividualUnknownAndImportantHitsStayIndependentAtAnyDensity()
        {
            var n = Numbers(); var t = new Target();
            foreach (var c in new[] { default(FeedbackNumberContext), Context(FeedbackNumberMode.Individual), Context(important: true) })
                for (int i = 0; i < 20; i++) Assert.That(Add(n, t, c, i * .01, window: .6).Merged, Is.False);
            Assert.That(n.MergedRequests, Is.Zero);
        }

        [Test]
        public void Action_ShotgunPelletsShareOneShotButDifferentBeatsNeverShare()
        {
            var n = Numbers(); var t = new Target(); var first = new Display();
            var a = Context(FeedbackNumberMode.Action);
            Assert.That(Add(n, t, a, 0, 12, display: first).Merged, Is.False);
            var merged = Add(n, t, a, .02, 8);
            Assert.That(merged.Display, Is.SameAs(first)); Assert.That(merged.Total, Is.EqualTo(20));
            Assert.That(merged.HitCount, Is.EqualTo(2));
            for (int beat = 2; beat <= 4; beat++)
                Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, beat), .02).Merged, Is.False);
        }

        [Test]
        public void SourceOwnerTargetStyleAndSessionAreIndependent()
        {
            var n = Numbers(); var t = new Target(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 1);
            Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, owner: 2), 1.01).Merged, Is.False);
            Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, source: 2), 1.01).Merged, Is.False);
            Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, group: 2), 1.01).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.01, style: 1).Merged, Is.False);
            Assert.That(Add(n, new Target(), c, 1.01).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.02).Total, Is.EqualTo(20));
        }

        [Test]
        public void Stream_FirstHitIsImmediate_SparseFireIsIndividual_DenseFireAccumulates()
        {
            var n = Numbers(); var t = new Target(); var c = Context();
            Assert.That(Add(n, t, c, 0).Merged, Is.False);
            Assert.That(Add(n, t, c, .3).Merged, Is.False);
            Assert.That(Add(n, t, c, .6).Merged, Is.False);
            bool merged = false;
            for (int i = 1; i <= 12; i++) merged |= Add(n, t, c, .6 + i * .03).Merged;
            Assert.That(merged, Is.True);
        }

        [Test]
        public void Stream_HysteresisAvoidsSwitchingAtOneThreshold_AndIdleStartsFresh()
        {
            var n = Numbers(); var t = new Target(); var c = Context();
            Add(n, t, c, 0, window: .6);
            Assert.That(Add(n, t, c, .09, window: .6).Merged, Is.True);
            Assert.That(Add(n, t, c, .21, window: .6).Merged, Is.True, "Dense streams tolerate an interval above the entry threshold.");
            Assert.That(Add(n, t, c, 1, window: .6).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.05, window: .6).Total, Is.EqualTo(20));
        }

        [Test]
        public void WindowsAreAnchoredAndCapped_EvenWhenBudgetsChange()
        {
            var n = Numbers(); var t = new Target(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 1, window: .15);
            Assert.That(Add(n, t, c, 1.1, window: .6).Total, Is.EqualTo(20));
            Assert.That(Add(n, t, c, 1.151, window: 10).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.7, window: 10).Merged, Is.True);
            Assert.That(Add(n, t, c, 1.752, window: 10).Merged, Is.False, "A dense stream cannot renew a display indefinitely.");
        }

        [Test]
        public void LateProjectilesRetainTheirCapturedSession_AfterTheNextSessionStarts()
        {
            var n = Numbers(); var t = new Target(); var old = Context(); var next = Context(group: 2);
            Add(n, t, old, 0); Add(n, t, next, .01);
            Assert.That(Add(n, t, old, .02).Total, Is.EqualTo(20));
            Assert.That(Add(n, t, next, .03).Total, Is.EqualTo(20));
        }

        [Test]
        public void ImportantHitDoesNotEnterOrEraseTheOrdinaryAccumulator()
        {
            var n = Numbers(); var t = new Target(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 0, 10);
            Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, important: true), .01, 1000).Merged, Is.False);
            Assert.That(Add(n, t, c, .02, 7).Total, Is.EqualTo(17));
        }

        [Test]
        public void CapacityRecyclesExpiredGroups_ForLongSessionsWithNewAttacks()
        {
            var n = Numbers(2); var t = new Target();
            for (int i = 1; i <= 1000; i++)
            {
                var c = Context(FeedbackNumberMode.Action, i);
                Assert.That(Add(n, t, c, i).Merged, Is.False);
                Assert.That(Add(n, t, c, i + .01).Total, Is.EqualTo(20));
            }
        }

        [Test]
        public void FullCapacityPreservesLiveGroups_AndFallsBackToIndividualOutput()
        {
            var n = Numbers(1); var t = new Target(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 0);
            Assert.That(Add(n, t, Context(FeedbackNumberMode.Action, 2), .01).Merged, Is.False);
            Assert.That(Add(n, t, c, .02).Total, Is.EqualTo(20));
        }

        [Test]
        public void PooledTargetsDisplaysAndBackwardsClocksStartFresh()
        {
            var n = Numbers(); var t = new Target(); var d = new Display(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 1, display: d); d.Version++;
            Assert.That(Add(n, t, c, 1.01, display: d).Merged, Is.False);
            t.Generation++;
            Assert.That(Add(n, t, c, 1.02).Merged, Is.False);
            Assert.That(Add(n, t, c, .1).Merged, Is.False);
        }

        [Test]
        public void InvalidInputDoesNotPoisonAnExistingWindow()
        {
            var n = Numbers(); var t = new Target(); var c = Context(FeedbackNumberMode.Action);
            Add(n, t, c, 1);
            Assert.That(Add(n, t, c, double.NaN).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.01, double.PositiveInfinity).Merged, Is.False);
            Assert.That(Add(n, t, c, 1.02).Total, Is.EqualTo(20));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FeedbackNumberPolicy(.2, .1, .6, .6));
        }

        [Test]
        public void StreamCadenceCountsShotsRatherThanPelletsOrDamageFragments()
        {
            var n = Numbers(); var t = new Target();
            for (int shot = 1; shot <= 10; shot++)
            {
                var c = new FeedbackNumberContext(FeedbackNumberMode.AdaptiveStream, 1, 1, 1, beatId: shot);
                Assert.That(Add(n, t, c, shot * .3, window: .6).Merged, Is.False);
                Assert.That(Add(n, t, c, shot * .3 + .001, window: .6).Total, Is.EqualTo(20));
                Assert.That(Add(n, t, c, shot * .3 + .002, window: .6).Total, Is.EqualTo(30));
            }
            var fast = new FeedbackNumberContext(FeedbackNumberMode.AdaptiveStream, 1, 1, 1, beatId: 11);
            Add(n, t, fast, 3.03, window: .6);
            fast = new FeedbackNumberContext(FeedbackNumberMode.AdaptiveStream, 1, 1, 1, beatId: 12);
            Add(n, t, fast, 3.06, window: .6);
            fast = new FeedbackNumberContext(FeedbackNumberMode.AdaptiveStream, 1, 1, 1, beatId: 13);
            Assert.That(Add(n, t, fast, 3.09, window: .6).Merged, Is.True);
        }

        [Test]
        public void WarmExpiredGroupChurnDoesNotAllocatePerHit()
        {
            var n = Numbers(2); var t = new Target(); var d = new Display();
            for (int i = 1; i <= 100; i++) Add(n, t, Context(FeedbackNumberMode.Action, i), i, display: d);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 101; i <= 1100; i++) Add(n, t, Context(FeedbackNumberMode.Action, i), i, display: d);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }
    }
}
