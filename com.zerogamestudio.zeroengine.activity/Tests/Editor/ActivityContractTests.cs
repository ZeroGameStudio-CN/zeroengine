using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ZeroEngine.Activity.Tests
{
    // Core contract: decisions are reproducible without a Unity world or a game's entity types.
    public sealed class ActivityContractTests
    {
        private static ActivityController Create(double delay = 0) =>
            new ActivityController(new ActivityPolicy(10, 30, 5, delay, 0.25));

        [TestCase(0, ActivityLevel.Full)]
        [TestCase(15, ActivityLevel.Full)]
        [TestCase(16, ActivityLevel.Reduced)]
        [TestCase(35, ActivityLevel.Reduced)]
        [TestCase(36, ActivityLevel.Dormant)]
        public void DistanceBands_HaveStableBoundaries(double distance, ActivityLevel expected)
        {
            Assert.That(Create().Evaluate(distance, 1, ActivityLevel.Dormant), Is.EqualTo(expected));
        }

        [Test]
        public void Waking_IsImmediate_WhileDowngradingRequiresStableDelay()
        {
            ActivityController state = Create(1);
            Assert.That(state.Evaluate(50, 0, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(50, 0.9, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(50, 1, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Dormant));
            Assert.That(state.Evaluate(5, 1.01, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(50, 1.02, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void Hysteresis_PreventsBoundaryThrashing()
        {
            ActivityController state = Create();
            state.Evaluate(40, 0, ActivityLevel.Dormant);
            Assert.That(state.Evaluate(31, 1, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Dormant));
            Assert.That(state.Evaluate(30, 2, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Reduced));
            Assert.That(state.Evaluate(34, 3, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Reduced));
            Assert.That(state.Evaluate(10, 4, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(14, 5, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void UnsafeOrUnobservedParticipant_RemainsFull()
        {
            ActivityController state = Create();
            Assert.That(state.Evaluate(100, 0, ActivityLevel.Full), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(100, 1, ActivityLevel.Reduced), Is.EqualTo(ActivityLevel.Reduced));
            Assert.That(state.Evaluate(double.PositiveInfinity, 2, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(state.Evaluate(double.NaN, 3, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void KeepAlive_IsOwnerScopedAndIdempotent()
        {
            ActivityController first = Create();
            ActivityController other = Create();
            IDisposable a = first.KeepActive();
            IDisposable b = first.KeepActive();
            a.Dispose(); a.Dispose();
            Assert.That(first.Evaluate(100, 0, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
            Assert.That(other.Evaluate(100, 0, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Dormant));
            b.Dispose();
            Assert.That(first.Evaluate(100, 1, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Dormant));
        }

        [Test]
        public void Decisions_UseSuppliedGameplayClock_NotFrameCount()
        {
            ActivityController state = Create();
            state.Evaluate(20, 0, ActivityLevel.Dormant);
            Assert.That(state.TakeDecisionTick(10), Is.True);
            for (int i = 0; i < 100; i++) Assert.That(state.TakeDecisionTick(10), Is.False, "Paused clock must not advance.");
            Assert.That(state.TakeDecisionTick(10.2), Is.False);
            Assert.That(state.TakeDecisionTick(10.25), Is.True);
            state.Wake();
            Assert.That(state.TakeDecisionTick(10.25), Is.True);
        }

        [Test]
        public void RewoundClock_DoesNotLeaveDecisionsStuck()
        {
            ActivityController state = Create();
            state.Evaluate(20, 10, ActivityLevel.Dormant);
            state.TakeDecisionTick(100);
            Assert.That(state.TakeDecisionTick(0), Is.True);
            Assert.That(state.Evaluate(100, 0, ActivityLevel.Dormant), Is.EqualTo(ActivityLevel.Full));
        }

        [Test]
        public void InvalidPolicy_IsRejectedAtConfigurationBoundary()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityPolicy(30, 10, 1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityPolicy(10, 30, -1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityPolicy(10, 30, 1, 1, 0));
            Assert.Throws<ArgumentException>(() => new ActivityController(default));
        }

        [Test]
        public void WorkBudget_LimitsCountAndElapsedWithoutDroppingRemainingWork()
        {
            double time = 0;
            var queue = new BudgetedWorkQueue(() => time);
            int count = 0;
            for (int i = 0; i < 10; i++) queue.Enqueue(i.ToString(), () => { count++; time += 1; });
            Assert.That(queue.Pump(4, 2), Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(8));
            Assert.That(queue.Pump(3, 100), Is.EqualTo(3));
            Assert.That(count, Is.EqualTo(5));
        }

        [Test]
        public void WorkBudget_ExpensiveSingleCallbackCannotBePreempted()
        {
            double time = 0;
            var queue = new BudgetedWorkQueue(() => time);
            queue.Enqueue("expensive", () => time += 100);
            queue.Enqueue("later", () => Assert.Fail("Must wait for another frame."));
            Assert.That(queue.Pump(4, 2), Is.EqualTo(1));
            Assert.That(time, Is.EqualTo(100));
        }

        [Test]
        public void Queue_DeduplicatesCancelsAndKeepsWorldsIndependent()
        {
            var first = new BudgetedWorkQueue(() => 0);
            var other = new BudgetedWorkQueue(() => 0);
            Assert.That(first.Enqueue("point", () => Assert.Fail()), Is.True);
            Assert.That(first.Enqueue("point", () => Assert.Fail()), Is.False);
            other.Enqueue("point", () => { });
            Assert.That(first.Cancel("point"), Is.True);
            Assert.That(first.Pump(2, 1), Is.Zero);
            Assert.That(other.Pump(2, 1), Is.EqualTo(1));
        }

        [Test]
        public void Queue_EligibilityDoesNotTurnUnvisitedContentIntoMandatoryWork()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            bool nearby = false;
            int count = 0;
            queue.Enqueue("point", () => count++, () => nearby);
            Assert.That(queue.Pump(4, 2), Is.Zero);
            Assert.That(queue.Count, Is.EqualTo(1));
            nearby = true;
            Assert.That(queue.Pump(4, 2), Is.EqualTo(1));
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Queue_AgingPreventsStarvation()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            bool oldRan = false;
            queue.Enqueue("old", () => oldRan = true);
            for (int i = 0; i < 20 && !oldRan; i++)
            {
                queue.Enqueue("near-" + i, () => { }, priority: 3);
                queue.Pump(1, 1);
            }
            Assert.That(oldRan, Is.True);
        }

        [Test]
        public void Queue_CallbackEnqueuesAndWorldCancellationCannotRunInSamePump()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            var calls = new List<string>();
            queue.Enqueue("first", () =>
            {
                calls.Add("first");
                queue.Clear();
                queue.Enqueue("new-world", () => calls.Add("new"));
            });
            queue.Enqueue("cancelled", () => Assert.Fail());
            Assert.That(queue.Pump(4, 2), Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { "first" }, calls);
            Assert.That(queue.Pump(4, 2), Is.EqualTo(1));
        }

        [Test]
        public void Queue_FailedPointDoesNotBlockOtherPoints()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            int failures = 0, successes = 0;
            queue.Enqueue("bad", () => throw new InvalidOperationException(), failed: _ => failures++);
            queue.Enqueue("good", () => successes++);
            Assert.That(queue.Pump(4, 2), Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(successes, Is.EqualTo(1));
        }

        [Test]
        public void Queue_InvalidEligibilityIsRemovedAndReported()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            int failures = 0, successes = 0;
            queue.Enqueue("bad", () => Assert.Fail(), () => throw new InvalidOperationException(), failed: _ => failures++);
            queue.Enqueue("good", () => successes++);
            Assert.That(queue.Pump(4, 2), Is.EqualTo(2));
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(successes, Is.EqualTo(1));
            Assert.That(queue.Count, Is.Zero);
        }

        [Test]
        public void Queue_RecursivePumpIsRejectedWithoutPoisoningLaterWork()
        {
            var queue = new BudgetedWorkQueue(() => 0);
            int failures = 0;
            queue.Enqueue("recursive", () => queue.Pump(1, 1), failed: _ => failures++);
            Assert.That(queue.Pump(1, 1), Is.EqualTo(1));
            Assert.That(failures, Is.EqualTo(1));
            queue.Enqueue("next", () => { });
            Assert.That(queue.Pump(1, 1), Is.EqualTo(1));
        }

        [Test]
        public void Queue_OwnerDisableCannotBeOverriddenByResume_AndDisposeIsTerminal()
        {
            bool ownerAvailable = false;
            var queue = new BudgetedWorkQueue(() => 0, ownerAvailable: () => ownerAvailable);
            queue.Resume();
            Assert.That(queue.Enqueue("closed", () => Assert.Fail()), Is.False);
            Assert.That(queue.Pump(1, 1), Is.Zero);
            ownerAvailable = true;
            Assert.That(queue.Enqueue("open", () => { }), Is.True);
            queue.Dispose();
            Assert.That(queue.Count, Is.Zero);
            Assert.That(queue.Pump(1, 1), Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => queue.Resume());
        }

        [Test]
        public void Queue_ClockFailureDoesNotLeavePumpLocked()
        {
            bool clockFails = true;
            var queue = new BudgetedWorkQueue(() => clockFails ? throw new InvalidOperationException() : 0);
            queue.Enqueue("work", () => { });
            Assert.Throws<InvalidOperationException>(() => queue.Pump(1, 1));
            clockFails = false;
            Assert.That(queue.Pump(1, 1), Is.EqualTo(1));
        }
    }
}
