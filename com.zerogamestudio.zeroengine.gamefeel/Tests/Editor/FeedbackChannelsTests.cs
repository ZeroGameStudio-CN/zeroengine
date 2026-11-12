using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    public sealed class FeedbackChannelsTests
    {
        private sealed class Target { internal int Generation; }
        private sealed class Display { internal uint Version = 1; internal bool Active = true; }
        private static FeedbackCadenceLimiter<Target, int> Cadence() => new(
            static t => t.Generation, static (t, generation) => t.Generation == generation);
        private static FeedbackNumberMerger<Target, int, Display, int> Numbers(int capacity = 32) => new(
            static t => t.Generation, static (t, generation) => t.Generation == generation,
            static (d, version) => d.Active && d.Version == version, capacity);
        private static TargetFeedbackReservations<Target, int> Particles() => new(4, 8, 8, 4);

        [Test]
        public void Cadence_FirstHitAndTargetsAreIndependent_AndPoolReuseStartsFresh()
        {
            var cadence = Cadence(); var a = new Target(); var b = new Target();
            Assert.That(cadence.TryAcquire(a, 1, .24), Is.True);
            Assert.That(cadence.TryAcquire(a, 1.1, .24), Is.False);
            Assert.That(cadence.TryAcquire(b, 1.1, .24), Is.True);
            a.Generation++;
            Assert.That(cadence.TryAcquire(a, 1.11, .24), Is.True);
            Assert.That(cadence.TryAcquire(a, 1.36, .24), Is.True);
        }

        [Test]
        public void Cadence_BypassAndInvalidTimeDoNotConsumeTheNextOrdinaryHit()
        {
            var cadence = Cadence(); var target = new Target();
            Assert.That(cadence.TryAcquire(target, double.NaN, .2), Is.False);
            Assert.That(cadence.TryAcquire(target, 1, .2, true), Is.True);
            Assert.That(cadence.TryAcquire(target, 1, .2), Is.True);
            Assert.That(cadence.TryAcquire(target, .5, .2), Is.True, "A restarted caller clock opens a new cadence.");
            cadence.Reset(target);
            Assert.That(cadence.TryAcquire(target, .51, .2), Is.True);
            cadence.Reset();
            Assert.That(cadence.TryAcquire(target, .52, .2), Is.True);
        }

        [Test]
        public void Numbers_FirstHitIsImmediate_ThenOnlyMatchingTargetAndStyleAccumulate()
        {
            var merger = Numbers(); var target = new Target(); var first = new Display();
            Assert.That(merger.Add(target, 1, first, 1, 10, 1, .375, false).Merged, Is.False);
            var merge = merger.Add(target, 1, new Display(), 1, 7, 1.1, .375, false);
            Assert.That(merge.Merged, Is.True); Assert.That(merge.Display, Is.SameAs(first));
            Assert.That(merge.Total, Is.EqualTo(17));
            Assert.That(merger.Add(target, 2, new Display(), 1, 9, 1.2, .375, false).Merged, Is.False);
            Assert.That(merger.Add(new Target(), 1, new Display(), 1, 11, 1.2, .375, false).Merged, Is.False);
            Assert.That(merger.Requests, Is.EqualTo(4)); Assert.That(merger.MergedRequests, Is.EqualTo(1));
        }

        [Test]
        public void Numbers_TargetAndDisplayReuseCannotAbsorbAnEarlierActivation()
        {
            var merger = Numbers(); var target = new Target(); var display = new Display();
            merger.Add(target, 0, display, 1, 10, 1, .6, false);
            display.Version++;
            Assert.That(merger.Add(target, 0, display, 2, 3, 1.1, .6, false).Merged, Is.False);
            target.Generation++;
            Assert.That(merger.Add(target, 0, new Display(), 1, 5, 1.2, .6, false).Merged, Is.False);
            Assert.That(merger.Add(target, 0, new Display(), 1, 2, 1.3, .6, false).Total, Is.EqualTo(7));
        }

        [Test]
        public void Numbers_ClockDurationAndExpiryChangesOpenFreshWindows()
        {
            var merger = Numbers(); var target = new Target(); var d = new Display();
            merger.Add(target, 0, d, 1, 10, 1, .15, false);
            Assert.That(merger.Add(target, 0, d, 1, 5, 1.1, .375, false).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 5, 1.2, .375, true).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 5, .5, .375, true).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 5, .875, .375, true).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 2, .9, .375, true).Total, Is.EqualTo(7));
        }

        [Test]
        public void Numbers_InvalidAndOverflowStylesKeepIndividualOutputsWithoutDroppingOtherWindows()
        {
            var merger = Numbers(1); var target = new Target(); var d = new Display();
            Assert.That(merger.Add(target, 0, d, 1, 10, 1, 0, false).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 10, double.NaN, .6, false).Merged, Is.False);
            merger.Add(target, 0, d, 1, 10, 1, .6, false);
            Assert.That(merger.Add(target, 1, d, 1, 5, 1.1, .6, false).Merged, Is.False);
            Assert.That(merger.Add(target, 0, d, 1, 7, 1.1, .6, false).Total, Is.EqualTo(17));
            merger.Clear();
            Assert.That(merger.Add(target, 0, d, 1, 9, 1.2, .6, false).Merged, Is.False);
            merger.Reset(); Assert.That(merger.Requests, Is.Zero);
        }

        [Test]
        public void UpdateQueue_ReentryPreservesTheNewerVersionAndDefersItUntilNextFlush()
        {
            var queue = new FeedbackUpdateQueue<Display, uint>(); var display = new Display();
            queue.Enqueue(display, 1); int calls = 0;
            queue.Flush((d, version) => { calls++; queue.Enqueue(d, 2); queue.Flush((_, _) => true); return false; });
            Assert.That(calls, Is.EqualTo(1));
            queue.Flush((d, version) => { calls++; Assert.That(version, Is.EqualTo(2)); return true; });
            Assert.That(calls, Is.EqualTo(2)); queue.Flush((_, _) => throw new Exception("Queue must be empty."));
        }

        [Test]
        public void UpdateQueue_ClearDuringCallbackEndsOldWorkAndKeepsNewlyPublishedWork()
        {
            var queue = new FeedbackUpdateQueue<Display, uint>(); var a = new Display(); var b = new Display();
            queue.Enqueue(a, 1); queue.Enqueue(b, 1); int calls = 0;
            queue.Flush((d, version) => { calls++; queue.Clear(); queue.Enqueue(a, 3); return false; });
            Assert.That(calls, Is.EqualTo(1));
            queue.Flush((d, version) => { calls++; Assert.That(d, Is.SameAs(a)); Assert.That(version, Is.EqualTo(3)); return true; });
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void ParticleReservations_OnlySuccessfulOutputsSpendTheirTargetBudget()
        {
            var particles = Particles(); var target = new Target(); var key = new FeedbackKey(1, 1);
            Assert.That(particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out var failed), Is.True);
            particles.Complete(failed, false, 1);
            Assert.That(particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out var first), Is.True);
            particles.Complete(first, true, 1);
            Assert.That(particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out _), Is.False);
            Assert.That(particles.TryBegin(new Target(), 1, key, 1, 1, 1, 1, false, out _), Is.True);
        }

        [Test]
        public void ParticleReservations_TargetResetSessionResetAndForeignTicketsAreSafe()
        {
            var particles = Particles(); var other = Particles(); var target = new Target(); var key = new FeedbackKey(1, 1);
            particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out var old);
            particles.Reset(target);
            Assert.That(particles.Complete(old, true, 1), Is.EqualTo(FeedbackReservationCommitResult.StaleReservation));
            particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out var current);
            Assert.That(other.Complete(current, true, 1), Is.EqualTo(FeedbackReservationCommitResult.WrongCoordinator));
            particles.Reset();
            Assert.That(particles.Complete(current, true, 1), Is.EqualTo(FeedbackReservationCommitResult.StaleReservation));
            Assert.That(particles.TryBegin(target, 1, key, 1, 1, 1, 1, false, out _), Is.True);
        }

        [Test]
        public void WarmTargetResets_ReuseReservationStorageWithoutRevivingOldTickets()
        {
            var particles = Particles(); var target = new Target(); var key = new FeedbackKey(1, 1);
            for (int i = 0; i < 100; i++)
            {
                particles.TryBegin(target, 1, key, 1, i, i, 1, false, out var ticket);
                particles.Complete(ticket, true, i); particles.Reset(target);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++)
            {
                particles.TryBegin(target, 1, key, 1, i, i, 1, false, out var ticket);
                particles.Complete(ticket, true, i); particles.Reset(target);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void WarmCadenceAndExpiredNumberWindows_DoNotAllocatePerRequest()
        {
            var cadence = Cadence(); var numbers = Numbers(); var target = new Target(); var display = new Display();
            for (int i = 0; i < 100; i++)
            { cadence.TryAcquire(target, i, .2); numbers.Add(target, 0, display, 1, 1, i, .2, false); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 100; i < 1100; i++)
            { cadence.TryAcquire(target, i, .2); numbers.Add(target, 0, display, 1, 1, i, .2, false); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "Expired windows reuse warmed target/style state.");
        }
    }
}
