using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    // Permanent regressions for indexed expiry/slot reuse and reusable reservation storage.
    public sealed class FeedbackIndexedStorageTests
    {
        [Test]
        public void ExtendingTheEarliestRetentionReordersExpiryWithoutLosingOtherKeys()
        {
            var state = new FeedbackCoordinator(4);
            var a = new FeedbackKey(1,1); var b = new FeedbackKey(2,1); var c = new FeedbackKey(3,1);
            state.Commit(state.Evaluate(a,1,0),1,out _);
            state.Commit(state.Evaluate(b,1,0),2,out _);
            state.Commit(state.Evaluate(c,1,0),3,out _);
            state.Commit(state.Evaluate(a,2,.5),4,out _);
            Assert.That(state.Evaluate(b,1,2.1).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(state.Count, Is.EqualTo(2));
            Assert.That(state.Evaluate(a,2,2.1).Decision, Is.EqualTo(FeedbackDecision.Duplicate));
            Assert.That(state.Evaluate(c,1,3.1).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(state.Count, Is.EqualTo(1));
        }

        [Test]
        public void CancellationAndReusedSlotsKeepAllPreviousHandlesStale()
        {
            var state = new FeedbackCoordinator(3);
            state.Commit(state.Evaluate(new FeedbackKey(1,1),1,0),1,out var first);
            state.Commit(state.Evaluate(new FeedbackKey(2,1),1,0),2,out var middle);
            state.Commit(state.Evaluate(new FeedbackKey(3,1),1,0),3,out var last);
            Assert.That(state.Cancel(middle), Is.True);
            state.Commit(state.Evaluate(new FeedbackKey(4,1),1,.1),4,out _);
            Assert.That(state.Cancel(middle), Is.False);
            Assert.That(state.Cancel(last), Is.True);
            Assert.That(state.Evaluate(new FeedbackKey(5,1),1,1.1).Decision, Is.EqualTo(FeedbackDecision.New));
            Assert.That(state.Cancel(first), Is.False);
            Assert.That(state.Count, Is.EqualTo(1));
        }

        private readonly struct Variant : IEquatable<Variant>
        {
            private readonly int id;
            public Variant(int id) => this.id = id;
            public bool Equals(Variant other) => id == other.id;
        }

        [Test]
        public void NestedReservationsCanCompleteOutOfOrderAndReuseReleasedMiddleSlots()
        {
            var state = new FeedbackReservationCoordinator<Variant>(2,8,3,1);
            var variant = new Variant(1);
            state.TryBegin(variant,new FeedbackKey(1,1),1,0,0,0,false,out var a);
            state.TryBegin(variant,new FeedbackKey(2,1),1,0,0,0,false,out var b);
            state.TryBegin(variant,new FeedbackKey(3,1),1,0,0,0,false,out var c);
            Assert.That(state.Complete(b,false,0), Is.EqualTo(FeedbackReservationCommitResult.Committed));
            Assert.That(state.TryBegin(variant,new FeedbackKey(4,1),1,0,0,0,false,out var d), Is.True);
            Assert.That(state.Complete(a,true,0), Is.EqualTo(FeedbackReservationCommitResult.Committed));
            Assert.That(state.Complete(c,true,0), Is.EqualTo(FeedbackReservationCommitResult.Committed));
            Assert.That(state.Complete(d,true,0), Is.EqualTo(FeedbackReservationCommitResult.Committed));
            Assert.That(state.InFlightCount, Is.Zero);
            Assert.That(state.FrameAcceptedCount, Is.EqualTo(3));
        }

        [Test]
        public void WarmedVariantExpiryAndScopeResetReuseTheirStorageWithoutAllocation()
        {
            var state = new FeedbackReservationCoordinator<Variant>(1,8,2,.1);
            state.TryBegin(new Variant(1),new FeedbackKey(1,1),1,0,0,0,false,out var warm);
            state.Complete(warm,true,0); state.Reset();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool success = true;
            for (int i=1;i<=500;i++)
            {
                success &= state.TryBegin(new Variant(i),new FeedbackKey(i,1),1,i,i,0,false,out var request);
                success &= state.Complete(request,true,i) == FeedbackReservationCommitResult.Committed;
                if (i%2==0) state.Reset();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.That(success, Is.True);
            Assert.That(allocated, Is.Zero);
        }
    }
}
