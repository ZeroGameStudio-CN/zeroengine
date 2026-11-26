using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;

namespace ZeroEngine.Interaction.Tests
{
    public sealed class InteractionAuthorityTests
    {
        private sealed class Clock : IInteractionClock { public double Now { get; set; } }
        private sealed class Range : IInteractionRange
        {
            public double Distance = 1;
            public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = Distance; return true; }
        }
        private sealed class Target : IInteractionTarget
        {
            public string Id { get; set; } = "target";
            public int Priority => 0;
            private bool available = true;
            public int AvailabilityChecks { get; private set; }
            public bool IsAvailable
            {
                get { AvailabilityChecks++; return available; }
                set { available = value; }
            }
            private InteractionPolicy policy = InteractionPolicy.Press;
            public InteractionPolicy Policy { get { if (ThrowPolicy) throw new InvalidOperationException("provider fault"); return policy; } set { policy = value; } }
            public InteractionCheck Admission = InteractionCheck.Permit;
            public bool Deferred, ClaimOnce, ThrowPolicy;
            public int Executions, Mutations, Refunds;
            public InteractionRequest Pending;
            public Action DuringMutation;
            public readonly List<InteractionGesture> Gestures = new List<InteractionGesture>();
            public InteractionCheck Check(InteractionContext context) => Admission;
            public void Execute(InteractionRequest request)
            {
                Executions++; Pending = request;
                if (Deferred) { request.Cancelled += _ => Refunds++; return; }
                Confirm(request);
            }
            public void Confirm(InteractionRequest request) => request.TryCommit(() =>
            { DuringMutation?.Invoke(); Mutations++; Gestures.Add(request.Context.Gesture); if (ClaimOnce) IsAvailable = false; });
        }
        private sealed class Fixture : IDisposable
        {
            public readonly InteractionAuthority Authority = new InteractionAuthority(17);
            public readonly Clock Clock = new Clock();
            public readonly Range Range = new Range();
            public readonly Target Target = new Target();
            public readonly InteractionAuthorityActor Actor;
            public readonly InteractionAuthorityTarget Lease;
            private long sequence;
            public Fixture(int history = 128)
            { Lease = Authority.RegisterTarget(Target, Target.Confirm); Actor = Authority.RegisterActor("actor", null, Clock, Range, 2, history); }
            public InteractionCommand Press() { long id = ++sequence; return Command(id, id, InteractionCommandKind.Press); }
            public InteractionCommand Next(long action, InteractionCommandKind kind) => Command(++sequence, action, kind);
            private InteractionCommand Command(long sequence, long action, InteractionCommandKind kind) =>
                new InteractionCommand(Authority.Epoch, Actor.Activation, sequence, action, Lease.Identity, kind);
            public void Dispose() { Authority.Dispose(); }
        }

        [Test] public void DuplicatePressAndConfirmationNeverRepeatTheBusinessMutation()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); Assert.That(f.Actor.Receive(press).InputAdmitted, Is.True);
            Assert.That(f.Actor.Receive(press).IsReplay, Is.True);
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            var confirm = f.Next(press.ActionId, InteractionCommandKind.Confirm);
            Assert.That(f.Actor.Receive(confirm).Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(f.Actor.Receive(confirm).IsReplay, Is.True);
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            Assert.That(f.Target.Executions, Is.EqualTo(1)); Assert.That(f.Target.Mutations, Is.EqualTo(1));
        }

        [Test] public void ReplayedPendingPressReturnsItsLaterAuthoritativeSuccess()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press);
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            var replay = f.Actor.Receive(press);
            Assert.That(replay.IsReplay, Is.True); Assert.That(replay.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(replay.IsPending, Is.False); Assert.That(f.Target.Mutations, Is.EqualTo(1));
        }

        [Test] public void SameSequenceWithChangedPayloadIsRefused()
        {
            using var f = new Fixture(); var press = f.Press(); f.Actor.Receive(press);
            var tampered = new InteractionCommand(press.Epoch, press.ActorActivation, press.Sequence, press.ActionId,
                new InteractionTargetIdentity("different", 9), press.Kind);
            var rejected = f.Actor.Receive(tampered);
            Assert.That(rejected.Rejection, Is.EqualTo(InteractionCommandRejection.ConflictingReplay));
            Assert.That(rejected.Outcome, Is.Null);
            Assert.That(rejected.IsHeld, Is.False);
            Assert.That(f.Target.Mutations, Is.EqualTo(1));
        }

        [Test] public void EvictedCommandsRemainStaleAndCannotGrantAgain()
        {
            using var f = new Fixture(2); var first = f.Press(); f.Actor.Receive(first);
            f.Actor.Receive(f.Next(first.ActionId, InteractionCommandKind.Release));
            var next = f.Press(); f.Actor.Receive(next); f.Actor.Receive(f.Next(next.ActionId, InteractionCommandKind.Release));
            Assert.That(f.Actor.Receive(first).Rejection, Is.EqualTo(InteractionCommandRejection.StaleSequence));
            Assert.That(f.Target.Mutations, Is.EqualTo(2));
        }

        [Test] public void InvalidEpochOrActorActivationCannotPoisonTheSequenceGate()
        {
            using var f = new Fixture(); var press = f.Press();
            var mirror = new InteractionOutcomeMirror(17, f.Actor.ActorId, f.Actor.Activation);
            Assert.That(mirror.TryApply(f.Actor.Receive(press)), Is.True);
            var oldEpoch = f.Actor.Receive(new InteractionCommand(16, press.ActorActivation, 100, press.ActionId, press.Target, InteractionCommandKind.Cancel));
            var oldActor = f.Actor.Receive(new InteractionCommand(17, press.ActorActivation + 1, 101, press.ActionId, press.Target, InteractionCommandKind.Cancel));
            Assert.That(oldEpoch.Rejection, Is.EqualTo(InteractionCommandRejection.WrongEpoch));
            Assert.That(oldEpoch.Epoch, Is.EqualTo(16));
            Assert.That(oldActor.Rejection, Is.EqualTo(InteractionCommandRejection.WrongActorActivation));
            Assert.That(oldActor.ActorActivation, Is.EqualTo(press.ActorActivation + 1));
            Assert.That(oldEpoch.Outcome, Is.Null);
            Assert.That(oldActor.Outcome, Is.Null);
            Assert.That(mirror.TryApply(oldEpoch), Is.False);
            Assert.That(mirror.TryApply(oldActor), Is.False);
            Assert.That(f.Actor.LastSequence, Is.EqualTo(press.Sequence));
            var release = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            Assert.That(mirror.TryApply(release), Is.True);
            Assert.That(mirror.LastReceiptSequence, Is.EqualTo(release.Sequence));
        }

        [Test] public void LegacyRejectWithCurrentHeadersCannotAdvanceTheMirror()
        {
            using var f = new Fixture();
            var press = f.Press();
            var result = f.Actor.Receive(press);
            var mirror = new InteractionOutcomeMirror(17, f.Actor.ActorId, f.Actor.Activation);
            var rejected = new InteractionCommandReceipt(17, f.Actor.ActorId, f.Actor.Activation, 100, press.ActionId,
                false, false, false, true, false, InteractionCommandRejection.WrongEpoch, result.Outcome);
            Assert.That(mirror.TryApply(rejected), Is.False);
            Assert.That(mirror.LastReceiptSequence, Is.Zero);
            Assert.That(mirror.LatestOutcome, Is.Null);
            Assert.That(mirror.TryApply(result), Is.True);
        }

        [Test] public void OldTargetActivationCannotResolveToTheReusedTarget()
        {
            using var f = new Fixture(); var old = f.Press(); f.Lease.Dispose();
            using var replacement = f.Authority.RegisterTarget(f.Target);
            Assert.That(replacement.Identity.Activation, Is.Not.EqualTo(old.Target.Activation));
            Assert.That(f.Actor.Receive(old).Rejection, Is.EqualTo(InteractionCommandRejection.TargetEnded));
            Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void ServerRangeWinsOverTheClientsChosenTarget()
        {
            using var f = new Fixture(); f.Range.Distance = 100;
            var reply = f.Actor.Receive(f.Press());
            Assert.That(reply.InputAdmitted, Is.False); Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void ExplicitTargetDoesNotBecomeAnotherNearbyTarget()
        {
            using var f = new Fixture(); var neighbor = new Target { Id = "neighbor" };
            using var lease = f.Authority.RegisterTarget(neighbor);
            f.Target.Policy = new InteractionPolicy(InteractionMode.TapOrHold);
            f.Authority.Tick();
            var press = f.Press();
            f.Actor.Receive(press);
            f.Authority.Tick();
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            Assert.That(f.Target.Mutations, Is.EqualTo(1)); Assert.That(neighbor.Mutations, Is.Zero);
            Assert.That(neighbor.AvailabilityChecks, Is.Zero);
        }

        [Test] public void ServerClockClassifiesAnEarlyReleaseAsATap()
        {
            using var f = new Fixture(); f.Target.Policy = new InteractionPolicy(InteractionMode.TapOrHold, 1, .2);
            var press = f.Press(); f.Actor.Receive(press); f.Clock.Now = .1;
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            Assert.That(f.Target.Gestures, Is.EqualTo(new[] { InteractionGesture.Tap }));
        }

        [Test] public void ServerClockPauseAndFrameJumpRetainExistingHoldRepeatBehavior()
        {
            using var f = new Fixture(); f.Target.Policy = new InteractionPolicy(InteractionMode.Hold, 1, .2);
            var press = f.Press(); f.Actor.Receive(press); f.Authority.Tick(); f.Authority.Tick();
            Assert.That(f.Target.Mutations, Is.Zero);
            f.Clock.Now = 1; f.Authority.Tick(); f.Clock.Now = 100; f.Authority.Tick(); f.Authority.Tick();
            Assert.That(f.Target.Gestures, Is.EqualTo(new[] { InteractionGesture.Hold, InteractionGesture.Repeat }));
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release)); f.Clock.Now = 101; f.Authority.Tick();
            Assert.That(f.Target.Mutations, Is.EqualTo(2));
        }

        [Test] public void CancelBeforeConfirmationReleasesOnceAndNeverPurchases()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press);
            var cancel = f.Next(press.ActionId, InteractionCommandKind.Cancel);
            f.Actor.Receive(cancel); f.Actor.Receive(cancel);
            var confirmation = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            Assert.That(confirmation.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled));
            Assert.That(f.Target.Refunds, Is.EqualTo(1)); Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void CancelAfterCommitDoesNotUndoOrRefundSuccess()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press);
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            var late = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Cancel));
            Assert.That(late.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(f.Target.Mutations, Is.EqualTo(1)); Assert.That(f.Target.Refunds, Is.Zero);
        }

        [Test] public void LateOldReleaseCannotEndANewerHeldAction()
        {
            using var f = new Fixture(); var first = f.Press(); f.Actor.Receive(first);
            f.Actor.Receive(f.Next(first.ActionId, InteractionCommandKind.Release));
            var second = f.Press(); f.Actor.Receive(second);
            var late = f.Actor.Receive(f.Next(first.ActionId, InteractionCommandKind.Release));
            Assert.That(late.Rejection, Is.EqualTo(InteractionCommandRejection.UnknownAction));
            Assert.That(f.Actor.Receive(f.Next(second.ActionId, InteractionCommandKind.Release)).Handled, Is.True);
        }

        [Test] public void LeavingRangeBeforeADeferredConfirmationRejectsMutation()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press); f.Range.Distance = 100;
            var result = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            Assert.That(result.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Denied)); Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void DisabledTargetCancelsPendingActionAndPoolReuseCannotConfirmIt()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press); f.Lease.Dispose();
            using var replacement = f.Authority.RegisterTarget(f.Target, f.Target.Confirm);
            var result = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            Assert.That(result.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled));
            Assert.That(f.Target.Refunds, Is.EqualTo(1)); Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void DisconnectInvalidatesTheReceiverAndReleasesPendingResources()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var press = f.Press(); f.Actor.Receive(press); f.Actor.Dispose();
            using var next = f.Authority.RegisterActor("actor", null, f.Clock, f.Range, 2);
            Assert.That(next.Activation, Is.Not.EqualTo(f.Actor.Activation));
            Assert.That(f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm)).Rejection,
                Is.EqualTo(InteractionCommandRejection.ActorEnded));
            Assert.That(f.Target.Refunds, Is.EqualTo(1)); Assert.That(f.Target.Mutations, Is.Zero);
        }

        [Test] public void TwoActorsContendingForOnePickupGrantExactlyOnce()
        {
            using var f = new Fixture(); f.Target.ClaimOnce = true;
            using var other = f.Authority.RegisterActor("other", null, f.Clock, f.Range, 2);
            f.Actor.Receive(f.Press());
            other.Receive(new InteractionCommand(17, other.Activation, 1, 1, f.Lease.Identity, InteractionCommandKind.Press));
            Assert.That(f.Target.Mutations, Is.EqualTo(1)); Assert.That(f.Target.Executions, Is.EqualTo(1));
        }

        [Test] public void LateAuthoritativeSuccessSurvivesLocalCancellationAndResponseReordering()
        {
            using var f = new Fixture(); f.Target.Deferred = true;
            var mirror = new InteractionOutcomeMirror(17, f.Actor.ActorId, f.Actor.Activation);
            var press = f.Press(); var first = f.Actor.Receive(press);
            var success = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Confirm));
            var cancelledLocally = f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Cancel));
            Assert.That(mirror.TryApply(cancelledLocally), Is.True);
            Assert.That(mirror.TryApply(first), Is.False); Assert.That(mirror.TryApply(success), Is.False);
            Assert.That(mirror.LatestOutcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
        }

        [Test] public void MirrorRejectsAnotherActorEpochOrActivation()
        {
            using var f = new Fixture(); var reply = f.Actor.Receive(f.Press());
            Assert.That(new InteractionOutcomeMirror(18, "actor", f.Actor.Activation).TryApply(reply), Is.False);
            Assert.That(new InteractionOutcomeMirror(17, "other", f.Actor.Activation).TryApply(reply), Is.False);
            Assert.That(new InteractionOutcomeMirror(17, "actor", f.Actor.Activation + 1).TryApply(reply), Is.False);
        }

        [Test] public void ProviderFailureIsRefusedAndReplaysCannotExecuteItAgain()
        {
            using var f = new Fixture(); f.Target.ThrowPolicy = true;
            var press = f.Press(); var failed = f.Actor.Receive(press);
            Assert.That(failed.Rejection, Is.EqualTo(InteractionCommandRejection.HandlerFault));
            f.Target.ThrowPolicy = false;
            Assert.That(f.Actor.Receive(press).Rejection, Is.EqualTo(InteractionCommandRejection.HandlerFault));
            Assert.That(f.Target.Mutations, Is.Zero);
            f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            f.Actor.Receive(f.Press()); Assert.That(f.Target.Mutations, Is.EqualTo(1));
        }

        [Test] public void MutationCannotReenterInputWhileAuthorityTickSettlesAHold()
        {
            using var f = new Fixture(); f.Target.Policy = new InteractionPolicy(InteractionMode.Hold, 1, .2);
            var press = f.Press(); f.Actor.Receive(press);
            var cancel = f.Next(press.ActionId, InteractionCommandKind.Cancel);
            InteractionCommandReceipt nested = default;
            f.Target.DuringMutation = () => nested = f.Actor.Receive(cancel);
            f.Clock.Now = 1; f.Authority.Tick();
            Assert.That(nested.Rejection, Is.EqualTo(InteractionCommandRejection.Busy));
            Assert.That(f.Actor.LastSequence, Is.EqualTo(press.Sequence));
            Assert.That(f.Actor.TryGetOutcome(press.ActionId, out var settled), Is.True);
            Assert.That(settled.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(f.Actor.Receive(cancel).Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(f.Target.Mutations, Is.EqualTo(1));
        }

        [Test] public void ReceiptCannotAttachAnOutcomeFromAnotherAction()
        {
            using var f = new Fixture(); var original = f.Actor.Receive(f.Press());
            var invalid = new InteractionCommandReceipt(original.Epoch, original.ActorId, original.ActorActivation,
                original.Sequence, original.ActionId + 1, true, true, false, false, false, InteractionCommandRejection.None, original.Outcome);
            var mirror = new InteractionOutcomeMirror(17, "actor", f.Actor.Activation);
            Assert.That(mirror.TryApply(invalid), Is.False); Assert.That(mirror.LatestOutcome.HasValue, Is.False);
            Assert.That(mirror.LastReceiptSequence, Is.Zero);
        }

        [Test] public void InvalidOutcomeCannotAdvanceTheProjectionRevision()
        {
            using var f = new Fixture(); var original = f.Actor.Receive(f.Press()).Outcome.Value;
            var invalid = new InteractionAuthorityOutcome(original.Epoch, original.ActorId, original.ActorActivation,
                original.ActionId, original.Revision + 1, 0, original.Target, original.Gesture, original.Status, null);
            var mirror = new InteractionOutcomeMirror(17, "actor", f.Actor.Activation);
            var badReceipt = new InteractionCommandReceipt(original.Epoch, original.ActorId, original.ActorActivation,
                100, original.ActionId, true, true, false, false, false, InteractionCommandRejection.None, invalid);
            Assert.That(mirror.TryApply(badReceipt), Is.False);
            Assert.That(mirror.LastReceiptSequence, Is.Zero);
            Assert.That(mirror.TryApply(invalid), Is.False); Assert.That(mirror.TryApply(original), Is.True);
        }

        [TestCase(InteractionMode.Press)] [TestCase(InteractionMode.Hold)] [TestCase(InteractionMode.TapOrHold)]
        public void OfflineAuthorityRetainsTheOriginalCoreGestureTrace(InteractionMode mode)
        {
            using var f = new Fixture(); f.Target.Policy = new InteractionPolicy(mode, 1, .2);
            using var world = new InteractionWorld(); var original = new Target { Policy = f.Target.Policy };
            using var lease = world.Register(original);
            using var agent = new InteractionAgent(world, "original", null, f.Clock, f.Range, 2);
            var press = f.Press();
            Assert.That(f.Actor.Receive(press).InputAdmitted, Is.EqualTo(agent.Press()));
            foreach (double time in new[] { .2, 1, 1.21, 100 })
            {
                f.Clock.Now = time; agent.Tick(); f.Authority.Tick();
                Assert.That(f.Actor.HoldProgress, Is.EqualTo(agent.Progress));
                Assert.That(f.Target.Gestures, Is.EqualTo(original.Gestures));
            }
            agent.Release(); f.Actor.Receive(f.Next(press.ActionId, InteractionCommandKind.Release));
            Assert.That(f.Target.Gestures, Is.EqualTo(original.Gestures));
        }
    }
}
