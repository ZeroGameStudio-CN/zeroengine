using System;
using NUnit.Framework;

namespace ZeroEngine.GameFeel.Tests
{
    // Core contract: model/other peak consumers retain independent nested hit bases and lifecycle ownership.
    public sealed class FeedbackPeakCoordinatorTests
    {
        [TestCase(1.6, .25)]
        [TestCase(.25, 1.6)]
        public void SameGroupKeepsPeakWithoutCombiningAnotherHitsBaseAndMultiplier(double firstMultiplier, double secondMultiplier)
        {
            var state = new FeedbackPeakCoordinator(); var key = new FeedbackKey(1,1);
            Assert.That(state.TryBegin(key, 1, 1, 1, 1, out var first), Is.True);
            state.TryResolve(1, firstMultiplier, 1, out _);
            Assert.That(state.TryBegin(key, 1, 1, 2, 1, out var second), Is.True);
            Assert.That(second.StartsNewGroup, Is.False);
            state.TryResolve(2, secondMultiplier, 1, out _);
            Assert.That(state.Strength(first.Group), Is.EqualTo(1.6).Within(1e-9));
            Assert.That(state.TryResolve(1, 9, 1, out _), Is.False);
            Assert.That(state.Strength(first.Group), Is.EqualTo(1.6).Within(1e-9));
        }

        [Test]
        public void NestedFinalizationPreservesEachBaseAndAllowsTheFirstHitToResolveToZero()
        {
            var state = new FeedbackPeakCoordinator(); var key = new FeedbackKey(1,1);
            state.TryBegin(key, 2, 2, 1, 1, out var first);
            state.TryBegin(key, .5, .5, 2, 1, out _);
            state.TryResolve(2, 1.5, 1, out _);
            state.TryBegin(key, .1, .1, 3, 1, out _);
            state.TryResolve(1, .25, 1, out _);
            state.FinalizeUnresolved(1);
            Assert.That(state.Strength(first.Group), Is.EqualTo(.75).Within(1e-9));
            Assert.That(state.PendingCount, Is.Zero);
            state.Reset();
            state.TryBegin(key, 1, 1, 4, 2, out var zero);
            state.TryResolve(4, 0, 2, out _);
            Assert.That(state.Strength(zero.Group), Is.Zero);
        }

        [Test]
        public void CapacityAndCompletedHistoryDoNotSuppressOtherTargetsOrPermitLateRestart()
        {
            var firstTarget = new FeedbackPeakCoordinator(activeCapacity:1);
            var secondTarget = new FeedbackPeakCoordinator(activeCapacity:1);
            var key = new FeedbackKey(1,1);
            firstTarget.TryBegin(key, 1, 1, 1, 1, out var first);
            Assert.That(firstTarget.TryBegin(new FeedbackKey(2,1), 1, 1, 2, 1, out _), Is.False);
            Assert.That(secondTarget.TryBegin(key, 1, 1, 1, 1, out var second), Is.True);
            Assert.That(firstTarget.End(second.Group, 1.1), Is.False);
            Assert.That(firstTarget.End(first.Group, 1.1), Is.True);
            Assert.That(firstTarget.TryBegin(key, 5, 5, 3, 1.2, out _), Is.False);
            Assert.That(firstTarget.TryBegin(new FeedbackKey(2,1), 1, 1, 4, 1.2, out _), Is.True);
        }

        [Test]
        public void FailedNewOutputAndResetReleaseOnlyTheirOwnContributions()
        {
            var state = new FeedbackPeakCoordinator(); var key = new FeedbackKey(1,1);
            state.TryBegin(key, 1, 1, 1, 1, out var first);
            Assert.That(state.CancelGroup(first.Group), Is.True);
            Assert.That(state.PendingCount, Is.Zero);
            Assert.That(state.TryBegin(key, 1, 1, 2, 1, out var retry), Is.True);
            state.Reset();
            Assert.That(state.End(retry.Group, 2), Is.False);
            Assert.That(state.TryResolve(2, 3, 2, out _), Is.False);
            Assert.That(state.TryBegin(key, 1, 1, 3, .1, out var fresh), Is.True);
            Assert.That(state.Strength(fresh.Group), Is.EqualTo(1));
        }

        [Test]
        public void WarmedContributionCyclesMakeNoPerHitManagedAllocation()
        {
            var state = new FeedbackPeakCoordinator();
            state.TryBegin(new FeedbackKey(1,1), 1, 1, 1, 0, out var warm);
            state.TryResolve(1, 1, 0, out _); state.End(warm.Group, .01); state.Reset();
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool success = true;
            for (int i = 1; i <= 1000; i++)
            {
                success &= state.TryBegin(new FeedbackKey(i,1), 1, 1, i, i, out var request);
                success &= state.TryResolve(i, 1.2, i, out _);
                success &= state.End(request.Group, i + .01);
                state.Reset();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(success, Is.True);
            Assert.That(allocated, Is.Zero);
        }
    }
}
