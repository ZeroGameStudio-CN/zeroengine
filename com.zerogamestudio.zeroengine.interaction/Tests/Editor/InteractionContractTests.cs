using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Tests
{
    public sealed class InteractionContractTests
    {
        private sealed class Clock : IInteractionClock { public double Now { get; set; } }
        private sealed class Range : IInteractionRange
        {
            public readonly Dictionary<string, double> Distances = new Dictionary<string, double>();
            public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) => Distances.TryGetValue(target.Id, out distance);
        }
        private sealed class Target : IInteractionTarget
        {
            public string Id { get; set; } = "target";
            public int Priority { get; set; }
            public bool IsAvailable { get; set; } = true;
            public InteractionPolicy Policy { get; set; } = InteractionPolicy.Press;
            public InteractionCheck Admission = InteractionCheck.Permit;
            public InteractionRequest Request;
            public int Executions, Mutations;
            public bool Deferred, Throw;
            public InteractionGesture LastGesture;
            public InteractionCheck Check(InteractionContext context) => Admission;
            public void Execute(InteractionRequest request)
            {
                Executions++; Request = request; LastGesture = request.Context.Gesture;
                if (Throw) throw new InvalidOperationException("action fault");
                if (!Deferred) request.TryCommit(() => Mutations++);
            }
        }

        private InteractionWorld world;
        private InteractionAgent agent;
        private Clock clock;
        private Range range;
        private Target target;
        private InteractionRegistration lease;
        private List<InteractionResult> results;

        [SetUp] public void SetUp()
        {
            world = new InteractionWorld(); clock = new Clock(); range = new Range(); target = new Target();
            range.Distances[target.Id] = 1; lease = world.Register(target);
            agent = new InteractionAgent(world, "actor-a", new object(), clock, range, 2);
            results = new List<InteractionResult>(); agent.Completed += results.Add; agent.RefreshFocus();
        }
        [TearDown] public void TearDown() { world.Dispose(); }

        [Test] public void PressIsLatchedUntilRelease()
        {
            Assert.That(agent.Press(), Is.True); Assert.That(agent.Press(), Is.False);
            Assert.That(target.Mutations, Is.EqualTo(1)); agent.Release(); agent.Press();
            Assert.That(target.Mutations, Is.EqualTo(2));
        }

        [Test] public void DenialHasAResultWithoutRunningTheAction()
        {
            target.Admission = InteractionCheck.Deny("Missing wallet.");
            Assert.That(agent.Press(), Is.False); Assert.That(target.Executions, Is.Zero);
            Assert.That(results[0].Status, Is.EqualTo(InteractionStatus.Denied));
            Assert.That(results[0].Reason, Is.EqualTo("Missing wallet."));
        }

        [Test] public void HoldUsesTheSuppliedClockAndDoesNotFireWhilePaused()
        {
            target.Policy = new InteractionPolicy(InteractionMode.Hold, 1);
            agent.Press(); clock.Now = .99; agent.Tick(); agent.Tick();
            Assert.That(target.Executions, Is.Zero); Assert.That(agent.Progress, Is.EqualTo(.99).Within(.001));
            clock.Now = 1; agent.Tick(); agent.Tick();
            Assert.That(target.Executions, Is.EqualTo(1)); Assert.That(target.LastGesture, Is.EqualTo(InteractionGesture.Hold));
        }

        [Test] public void ReleasingAnEarlyHoldCancelsIt()
        {
            target.Policy = new InteractionPolicy(InteractionMode.Hold, 1);
            agent.Press(); clock.Now = .5; agent.Release(); clock.Now = 2; agent.Tick();
            Assert.That(target.Executions, Is.Zero); Assert.That(agent.Progress, Is.Zero);
        }

        [Test] public void TapOrHoldExecutesShortTapOnReleaseOnly()
        {
            target.Policy = new InteractionPolicy(InteractionMode.TapOrHold, 1);
            agent.Press(); Assert.That(target.Executions, Is.Zero); clock.Now = .2; agent.Release(); agent.Release();
            Assert.That(target.Executions, Is.EqualTo(1)); Assert.That(target.LastGesture, Is.EqualTo(InteractionGesture.Tap));
        }

        [Test] public void CompletedHoldDoesNotAlsoTapOnRelease()
        {
            target.Policy = new InteractionPolicy(InteractionMode.TapOrHold, 1);
            agent.Press(); clock.Now = 1; agent.Tick(); agent.Release();
            Assert.That(target.Executions, Is.EqualTo(1)); Assert.That(target.LastGesture, Is.EqualTo(InteractionGesture.Hold));
        }

        [Test] public void ReleaseAtTheThresholdDoesNotMisclassifyAHoldAsATap()
        {
            target.Policy = new InteractionPolicy(InteractionMode.TapOrHold, 1);
            agent.Press(); clock.Now = 1; agent.Release();
            Assert.That(target.Executions, Is.EqualTo(1)); Assert.That(target.LastGesture, Is.EqualTo(InteractionGesture.Hold));
        }

        [Test] public void ScopeDisposalFinishesOtherActorsWhenAnObserverThrows()
        {
            target.Deferred = true; agent.Press(); agent.Completed += result => throw new InvalidOperationException("observer fault");
            var otherRange = new Range(); otherRange.Distances[target.Id] = 1;
            var second = new InteractionAgent(world, "actor-b", null, clock, otherRange, 2);
            second.Press(); var secondRequest = target.Request;
            Assert.Throws<AggregateException>(() => world.Dispose());
            Assert.That(second.IsDisposed, Is.True); Assert.That(secondRequest.IsSettled, Is.True); Assert.That(world.Count, Is.Zero);
        }

        [Test] public void RepeatDoesNotCatchUpWithAnUnboundedBurst()
        {
            target.Policy = new InteractionPolicy(InteractionMode.Hold, 1, .2);
            agent.Press(); clock.Now = 1; agent.Tick(); clock.Now = 100; agent.Tick();
            Assert.That(target.Executions, Is.EqualTo(2)); agent.Tick(); Assert.That(target.Executions, Is.EqualTo(2));
            clock.Now = 100.2; agent.Tick(); Assert.That(target.Executions, Is.EqualTo(3));
            agent.Release(); clock.Now = 101; agent.Tick(); Assert.That(target.Executions, Is.EqualTo(3));
        }

        [Test] public void FocusChangeDoesNotTransferAHeldPressToTheNewTarget()
        {
            target.Policy = new InteractionPolicy(InteractionMode.Hold, 1);
            agent.Press(); var next = new Target { Id = "next", Priority = 10, Policy = target.Policy };
            range.Distances[next.Id] = 1; world.Register(next); agent.Tick(); clock.Now = 2; agent.Tick();
            Assert.That(target.Executions + next.Executions, Is.Zero);
            Assert.That(agent.Press(), Is.False); agent.Release(); agent.Press(); clock.Now = 3; agent.Tick();
            Assert.That(next.Executions, Is.EqualTo(1));
        }

        [Test] public void PriorityThenDistanceThenStableIdResolveFocusDeterministically()
        {
            var other = new Target { Id = "a", Priority = 1 }; range.Distances[other.Id] = 4;
            var otherLease = world.Register(other); agent.RefreshFocus(); Assert.That(agent.Focus, Is.SameAs(otherLease));
            target.Priority = 1; agent.RefreshFocus(); Assert.That(agent.Focus, Is.SameAs(lease));
            range.Distances[other.Id] = 1; agent.RefreshFocus(); Assert.That(agent.Focus, Is.SameAs(otherLease));
        }

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1)] [TestCase(4.01)]
        public void InvalidOrOutsideDistanceCannotBecomeFocus(double distance)
        { range.Distances[target.Id] = distance; agent.RefreshFocus(); Assert.That(agent.Focus, Is.Null); Assert.That(agent.Press(), Is.False); }

        [Test] public void TwoActorsOwnIndependentFocusAndInput()
        {
            var other = new Target { Id = "other" }; var otherLease = world.Register(other);
            var otherRange = new Range(); otherRange.Distances[other.Id] = 0;
            using (var second = new InteractionAgent(world, "actor-b", null, clock, otherRange, 2))
            {
                second.RefreshFocus(); Assert.That(agent.Focus, Is.SameAs(lease)); Assert.That(second.Focus, Is.SameAs(otherLease));
                second.Press(); agent.Press(); Assert.That(target.Mutations, Is.EqualTo(1)); Assert.That(other.Mutations, Is.EqualTo(1));
            }
        }

        [Test] public void ADeferredRequestCanCommitExactlyOnce()
        {
            target.Deferred = true; agent.Press(); var request = target.Request; agent.Release();
            Assert.That(request.IsCurrent, Is.True); Assert.That(request.TryCommit(() => target.Mutations++), Is.True);
            Assert.That(request.TryCommit(() => target.Mutations++), Is.False); Assert.That(request.TryDeny("late"), Is.False);
            Assert.That(target.Mutations, Is.EqualTo(1)); Assert.That(results.Count, Is.EqualTo(1));
        }

        [Test] public void PendingRequestBlocksAnotherPressAndRepeat()
        {
            target.Deferred = true; target.Policy = new InteractionPolicy(InteractionMode.Hold, .5, .1);
            agent.Press(); clock.Now = .5; agent.Tick(); clock.Now = 5; agent.Tick();
            Assert.That(target.Executions, Is.EqualTo(1)); agent.Release();
            Assert.That(target.Request.IsCurrent, Is.False); Assert.That(results[0].Status, Is.EqualTo(InteractionStatus.Cancelled));
        }

        [Test] public void LeavingRangeRejectsALateCommitEvenBeforeTheNextTick()
        {
            target.Deferred = true; agent.Press(); range.Distances[target.Id] = 100;
            Assert.That(target.Request.TryCommit(() => target.Mutations++), Is.False);
            Assert.That(target.Mutations, Is.Zero); Assert.That(agent.Pending, Is.Null);
        }

        [Test] public void ConditionsAreRecheckedAtCommit()
        {
            target.Deferred = true; agent.Press(); target.Admission = InteractionCheck.Deny("Wallet changed.");
            Assert.That(target.Request.TryCommit(() => target.Mutations++), Is.False);
            Assert.That(target.Mutations, Is.Zero); Assert.That(results[0].Reason, Is.EqualTo("Wallet changed."));
        }

        [Test] public void AvailabilityRevocationCancelsPendingWork()
        {
            target.Deferred = true; agent.Press(); target.IsAvailable = false; agent.Tick();
            Assert.That(target.Request.TryCommit(() => target.Mutations++), Is.False);
            Assert.That(target.Mutations, Is.Zero); Assert.That(agent.Focus, Is.Null);
        }

        [Test] public void PooledActivationRejectsItsOldRequestAndOldLease()
        {
            target.Deferred = true; agent.Press(); var stale = target.Request; var oldLease = lease;
            oldLease.Dispose(); lease = world.Register(target); oldLease.Dispose();
            agent.Release(); agent.RefreshFocus(); agent.Press();
            Assert.That(lease.Activation, Is.GreaterThan(oldLease.Activation)); Assert.That(world.Count, Is.EqualTo(1));
            Assert.That(stale.TryCommit(() => target.Mutations++), Is.False);
            Assert.That(target.Request.TryCommit(() => target.Mutations++), Is.True); Assert.That(target.Mutations, Is.EqualTo(1));
        }

        [Test] public void CancellationCallbacksRunOnceAndSeeASettledRequest()
        {
            target.Deferred = true; agent.Press(); var request = target.Request; int cancelled = 0;
            request.Cancelled += reason => { cancelled++; Assert.That(request.IsCurrent, Is.False); };
            agent.Cancel(); agent.Cancel(); lease.Dispose();
            Assert.That(cancelled, Is.EqualTo(1)); Assert.That(results.Count, Is.EqualTo(1));
        }

        [Test] public void ActorDisposalInvalidatesDelayedRequestsAndReleasesIdentity()
        {
            target.Deferred = true; agent.Press(); var stale = target.Request; agent.Dispose();
            Assert.That(stale.TryCommit(() => target.Mutations++), Is.False);
            using (var replacement = new InteractionAgent(world, "actor-a", null, clock, range, 2))
            { replacement.Press(); Assert.That(target.Request.IsCurrent, Is.True); }
        }

        [Test] public void WorldDisposalCancelsActorsAndRefusesFurtherRegistration()
        {
            target.Deferred = true; agent.Press(); var stale = target.Request; world.Dispose();
            Assert.That(agent.IsDisposed, Is.True); Assert.That(stale.IsCurrent, Is.False);
            Assert.Throws<ObjectDisposedException>(() => world.Register(new Target { Id = "new" }));
        }

        [Test] public void ClockResetCancelsTheHoldInsteadOfReusingItsOldStart()
        {
            clock.Now = 10; agent.Tick(); target.Policy = new InteractionPolicy(InteractionMode.Hold, 1);
            agent.Press(); clock.Now = 0; agent.Tick(); clock.Now = 11; agent.Tick(); Assert.That(target.Executions, Is.Zero);
        }

        [Test] public void ClockResetBeforeTheNextTickRejectsDelayedCommit()
        {
            target.Deferred = true; clock.Now = 10; agent.Press(); clock.Now = 1;
            Assert.That(target.Request.TryCommit(() => target.Mutations++), Is.False);
            Assert.That(target.Mutations, Is.Zero); Assert.That(results[0].Status, Is.EqualTo(InteractionStatus.Cancelled));
        }

        [Test] public void ReleaseAfterAClockResetCannotBecomeAShortTap()
        {
            target.Policy = new InteractionPolicy(InteractionMode.TapOrHold, 1);
            clock.Now = 10; agent.Press(); clock.Now = 1; agent.Release();
            Assert.That(target.Executions, Is.Zero);
        }

        [Test] public void ActionFaultIsReportedAndDoesNotLeaveAnActorBusy()
        {
            target.Throw = true; Assert.That(agent.Press(), Is.False);
            Assert.That(results[0].Status, Is.EqualTo(InteractionStatus.Faulted)); Assert.That(agent.Pending, Is.Null);
            target.Throw = false; agent.Release(); agent.Press(); Assert.That(target.Mutations, Is.EqualTo(1));
        }

        [Test] public void CommitFaultCannotBeRetried()
        {
            target.Deferred = true; agent.Press(); var request = target.Request;
            Assert.That(request.TryCommit(() => throw new InvalidOperationException("mutation fault")), Is.False);
            Assert.That(results[0].Status, Is.EqualTo(InteractionStatus.Faulted));
            Assert.That(request.TryCommit(() => target.Mutations++), Is.False); Assert.That(agent.Pending, Is.Null);
        }

        [Test] public void CommitReentryCannotTriggerAnotherMutation()
        {
            target.Deferred = true; agent.Press(); agent.Release();
            target.Request.TryCommit(() => { agent.Press(); target.Mutations++; });
            Assert.That(target.Executions, Is.EqualTo(1)); Assert.That(target.Mutations, Is.EqualTo(1));
        }

        [Test] public void DuplicateTargetRegistrationDoesNotReplaceTheExistingOwner()
        {
            Assert.Throws<InvalidOperationException>(() => world.Register(new Target()));
            Assert.That(world.Count, Is.EqualTo(1)); Assert.That(lease.IsRegistered, Is.True);
        }

        [Test] public void DuplicateActorIdentityIsRejectedWithinTheScope()
        { Assert.Throws<InvalidOperationException>(() => new InteractionAgent(world, "actor-a", null, clock, range, 2)); }
    }
}
