using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;

namespace ZeroEngine.Interaction.Tests
{
    public sealed class InteractionExecutionTests
    {
        private sealed class Clock : IInteractionClock { public double Now => 0; }
        private sealed class Range : IInteractionRange
        {
            public double Distance = 1;
            public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = Distance; return true; }
        }
        private sealed class Target : IInteractionTarget
        {
            public string Id { get; set; } = "external";
            public int Priority => 0;
            public bool IsAvailable => true;
            public InteractionPolicy Policy => InteractionPolicy.Press;
            public bool Allowed = true;
            public IInteractionExecution Execution;
            public Action<IInteractionExecution> Started;
            public InteractionCheck Check(InteractionContext context) => Allowed ? InteractionCheck.Permit : InteractionCheck.Deny("Access revoked.");
            public void Execute(InteractionRequest request) { Execution = request; Started?.Invoke(request); }
        }
        private sealed class Fixture : IDisposable
        {
            public readonly InteractionWorld World = new InteractionWorld();
            public readonly Target Target = new Target();
            public readonly Range Range = new Range();
            public readonly InteractionAgent Agent;
            public readonly InteractionRegistration Lease;
            public Fixture()
            { Lease = World.Register(Target); Agent = new InteractionAgent(World, "actor", new object(), new Clock(), Range, 2); Agent.Press(); Agent.Release(); }
            public IInteractionExecution Execution => Target.Execution;
            public void Dispose() { Agent.Dispose(); World.Dispose(); }
        }

        [Test] public void IndependentContentUsesThePortAndCompletesOnceAfterRelease()
        {
            using var f = new Fixture(); IInteractionExecution execution = f.Execution;
            var lifetime = execution.LifetimeToken; int writes = 0, notifications = 0, cancellations = 0;
            string observedActor = null; long observedRequest = 0; bool tokenEndedBeforeNotification = false;
            execution.Settled += result =>
            {
                notifications++; observedActor = result.Context.ActorId; observedRequest = result.RequestId;
                tokenEndedBeforeNotification = lifetime.IsCancellationRequested;
            };
            ((InteractionRequest)execution).Cancelled += _ => cancellations++;
            Assert.That(execution.IsCurrent, Is.True); Assert.That(execution.Outcome.HasValue, Is.False);
            Assert.That(execution.TryCommitResult(() => { writes++; return InteractionCheck.Permit; }), Is.True);
            Assert.That(execution.TryCommit(() => writes++), Is.False); Assert.That(execution.TryCancel(), Is.False);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(execution.IsCurrent, Is.False); Assert.That(execution.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(writes, Is.EqualTo(1)); Assert.That(notifications, Is.EqualTo(1)); Assert.That(cancellations, Is.Zero);
            Assert.That(observedActor, Is.EqualTo("actor")); Assert.That(observedRequest, Is.EqualTo(execution.Id));
            Assert.That(tokenEndedBeforeNotification, Is.True); Assert.That(execution.NotificationFailure, Is.Null);
        }

        [TestCase(InteractionStatus.Denied)] [TestCase(InteractionStatus.Cancelled)] [TestCase(InteractionStatus.Faulted)]
        public void EveryTerminalPathCancelsAsyncLifetimeAndNotifiesExactlyOnce(InteractionStatus status)
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken;
            int notifications = 0, callbacks = 0; token.Register(() => callbacks++); InteractionStatus? observed = null;
            execution.Settled += result => { notifications++; observed = result.Status; };
            bool ended = status == InteractionStatus.Denied ? execution.TryDeny("Not admitted.") :
                status == InteractionStatus.Cancelled ? execution.TryCancel() : execution.TryFault(new InvalidOperationException("Content failed."));
            Assert.That(ended, Is.True); Assert.That(execution.TryFault(new Exception("late")), Is.False);
            Assert.That(execution.TryCommit(), Is.False); Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(status));
            Assert.That(notifications, Is.EqualTo(1)); Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo(status)); Assert.That(execution.NotificationFailure, Is.Null);
        }

        [TestCase("target")] [TestCase("actor")] [TestCase("world")] [TestCase("range")] [TestCase("condition")]
        public void LifetimeEndsWhenTheOriginalAdmissionIsInvalidated(string invalidation)
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken;
            switch (invalidation)
            {
                case "target": f.Lease.Dispose(); break;
                case "actor": f.Agent.Dispose(); break;
                case "world": f.World.Dispose(); break;
                case "range": f.Range.Distance = 100; f.Agent.Tick(); break;
                case "condition": f.Target.Allowed = false; f.Agent.Tick(); break;
            }
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(execution.TryCommit(), Is.False);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled));
        }

        [Test] public void RejectedBusinessCompletionIsTerminalWithoutInvokingItAgain()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken; int attempts = 0;
            Assert.That(execution.TryCommitResult(() => { attempts++; return InteractionCheck.Deny("Provider rejected."); }), Is.False);
            Assert.That(execution.TryCommit(() => attempts++), Is.False);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Denied));
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(attempts, Is.EqualTo(1));
        }

        [Test] public void BusinessFaultIsTerminalAndDoesNotLeaveAnAsyncLifetimeAlive()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken; int attempts = 0;
            Assert.That(execution.TryCommit(() => { attempts++; throw new InvalidOperationException("Provider failed."); }), Is.False);
            Assert.That(execution.TryCommit(() => attempts++), Is.False);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Faulted));
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(attempts, Is.EqualTo(1));
        }

        [Test] public void LifetimeEndsAfterTheGuardedBusinessCallbackRatherThanInterruptingIt()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken;
            bool callbackRan = false, endedInside = true, sameInside = false;
            execution.TryCommit(() =>
            {
                callbackRan = true; endedInside = token.IsCancellationRequested;
                sameInside = execution.LifetimeToken == token;
            });
            Assert.That(callbackRan, Is.True); Assert.That(endedInside, Is.False); Assert.That(sameInside, Is.True);
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test] public void FailingNewNotificationsCannotHideSuccessOrPreventOtherCleanup()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken;
            int cleanup = 0, writes = 0;
            token.Register(() => throw new InvalidOperationException("Async cleanup failed."));
            token.Register(() => cleanup++);
            execution.Settled += _ => throw new InvalidOperationException("Content observer failed.");
            execution.Settled += _ => cleanup++;
            Assert.That(execution.TryCommit(() => writes++), Is.True);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(execution.NotificationFailure, Is.TypeOf<AggregateException>());
            Assert.That(cleanup, Is.EqualTo(2)); Assert.That(writes, Is.EqualTo(1));
        }

        [Test] public void LegacyObserverFailureStillPublishesTheTerminalPortState()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken; int notifications = 0;
            f.Agent.Completed += _ => throw new InvalidOperationException("Legacy observer failed.");
            execution.Settled += _ => notifications++;
            Assert.Throws<InvalidOperationException>(() => execution.TryCommit());
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(notifications, Is.EqualTo(1));
        }

        [Test] public void LegacyCancellationFailureStillEndsThePortAndClearsPendingWork()
        {
            using var f = new Fixture(); var execution = f.Execution; var token = execution.LifetimeToken;
            ((InteractionRequest)execution).Cancelled += _ => throw new InvalidOperationException("Legacy cleanup failed.");
            Assert.Throws<InvalidOperationException>(() => execution.TryCancel());
            Assert.That(f.Agent.Pending, Is.Null); Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled));
        }

        [Test] public void CapturedExecutionCannotCompleteTheReplacementTargetActivation()
        {
            using var f = new Fixture(); var old = f.Execution; var token = old.LifetimeToken;
            f.Lease.Dispose(); using var replacement = f.World.Register(f.Target);
            Assert.That(f.Agent.Press(), Is.True); f.Agent.Release(); var current = f.Execution; int writes = 0;
            Assert.That(current.Context.Activation, Is.Not.EqualTo(old.Context.Activation));
            Assert.That(old.TryCommit(() => writes++), Is.False); Assert.That(current.TryCommit(() => writes++), Is.True);
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(writes, Is.EqualTo(1));
        }

        [Test] public void NullFaultDoesNotEndAnAdmittedExecution()
        {
            using var f = new Fixture(); Assert.Throws<ArgumentNullException>(() => f.Execution.TryFault(null));
            Assert.That(f.Execution.IsCurrent, Is.True); Assert.That(f.Execution.TryCommit(), Is.True);
        }

        [Test] public void ReentrantCancellationCleanupCannotAttributeAnOldOutcomeToTheNewAction()
        {
            using var authority = new InteractionAuthority(9);
            var first = new Target { Id = "first" }; var second = new Target { Id = "second" };
            using var firstLease = authority.RegisterTarget(first); using var secondLease = authority.RegisterTarget(second);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            actor.Receive(Command(actor, firstLease, 1, 1, InteractionCommandKind.Press));
            actor.Receive(Command(actor, firstLease, 2, 1, InteractionCommandKind.Release));
            ((InteractionRequest)first.Execution).Cancelled += _ =>
            {
                actor.Receive(Command(actor, secondLease, 3, 3, InteractionCommandKind.Press));
                actor.Receive(Command(actor, secondLease, 4, 3, InteractionCommandKind.Release));
            };
            first.Execution.TryCancel();
            Assert.That(actor.TryGetOutcome(1, out var old), Is.True); Assert.That(old.Status, Is.EqualTo(InteractionStatus.Cancelled));
            Assert.That(old.Target.Id, Is.EqualTo("first")); Assert.That(actor.TryGetOutcome(3, out _), Is.False);
            Assert.That(second.Execution.TryCommit(), Is.True); Assert.That(actor.TryGetOutcome(3, out var current), Is.True);
            Assert.That(current.Status, Is.EqualTo(InteractionStatus.Succeeded)); Assert.That(current.Target.Id, Is.EqualTo("second"));
        }

        [Test] public void AnEndingRequestRetainsItsActionWhileReentrantCommandsTrimHistory()
        {
            using var authority = new InteractionAuthority(9); var target = new Target();
            using var lease = authority.RegisterTarget(target);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2, 2);
            actor.Receive(Command(actor, lease, 1, 1, InteractionCommandKind.Press));
            actor.Receive(Command(actor, lease, 2, 1, InteractionCommandKind.Release));
            var old = target.Execution;
            ((InteractionRequest)old).Cancelled += _ =>
            {
                target.Started = execution => execution.TryCommit();
                for (int id = 3; id <= 7; id += 2)
                {
                    actor.Receive(Command(actor, lease, id, id, InteractionCommandKind.Press));
                    actor.Receive(Command(actor, lease, id + 1, id, InteractionCommandKind.Release));
                }
            };
            old.TryCancel();
            Assert.That(actor.TryGetOutcome(1, out var result), Is.True); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Cancelled));
            Assert.That(actor.TryGetOutcome(7, out result), Is.True); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Succeeded));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void SuccessfulCommitStillReportsTheResultWhenItsAuthorityEnds(bool deferred, bool closeWorld)
        {
            using var authority = new InteractionAuthority(9);
            var target = new Target();
            using var lease = authority.RegisterTarget(target);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            int writes = 0;
            bool committed = false;
            void Finish(IInteractionExecution execution)
            {
                committed = execution.TryCommit(() =>
                {
                    writes++;
                    if (closeWorld) authority.Dispose();
                    else actor.Dispose();
                });
            }
            if (!deferred) target.Started = Finish;

            var press = Command(actor, lease, 1, 1, InteractionCommandKind.Press);
            var receipt = actor.Receive(press);
            if (deferred)
            {
                Assert.That(receipt.IsPending, Is.True);
                actor.Receive(Command(actor, lease, 2, 1, InteractionCommandKind.Release));
                Finish(target.Execution);
            }
            else Assert.That(receipt.Outcome?.Status, Is.EqualTo(InteractionStatus.Succeeded));

            Assert.That(committed, Is.True);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(target.Execution.Outcome?.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(actor.TryGetOutcome(1, out var outcome), Is.True);
            Assert.That(outcome.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(outcome.ActorActivation, Is.EqualTo(actor.Activation));
            Assert.That(outcome.ExecutionId, Is.EqualTo(target.Execution.Id));
            var notifications = new List<InteractionAuthorityOutcome>();
            actor.DrainOutcomes(notifications);
            Assert.That(notifications.Count, Is.EqualTo(1));
            Assert.That(actor.Receive(press).Outcome, Is.Null);
            Assert.That(target.Execution.TryCommit(() => writes++), Is.False);
            Assert.That(writes, Is.EqualTo(1));
        }

        [Test] public void EndingTheWorldInvalidatesAllPendingActorsBeforeTheirCleanup()
        {
            using var authority = new InteractionAuthority(9);
            var first = new Target { Id = "first" };
            var second = new Target { Id = "second" };
            using var firstLease = authority.RegisterTarget(first);
            using var secondLease = authority.RegisterTarget(second);
            using var actorA = authority.RegisterActor("a", null, new Clock(), new Range(), 2);
            using var actorB = authority.RegisterActor("b", null, new Clock(), new Range(), 2);
            actorA.Receive(Command(actorA, firstLease, 1, 1, InteractionCommandKind.Press));
            actorB.Receive(Command(actorB, secondLease, 1, 1, InteractionCommandKind.Press));
            int otherActorsStillCurrent = 0;
            ((InteractionRequest)first.Execution).Cancelled += _ => { if (second.Execution.IsCurrent) otherActorsStillCurrent++; };
            ((InteractionRequest)second.Execution).Cancelled += _ => { if (first.Execution.IsCurrent) otherActorsStillCurrent++; };
            authority.Dispose();
            Assert.That(otherActorsStillCurrent, Is.Zero);
            Assert.That(first.Execution.Outcome?.Status, Is.EqualTo(InteractionStatus.Cancelled));
            Assert.That(second.Execution.Outcome?.Status, Is.EqualTo(InteractionStatus.Cancelled));
        }

        [TestCase(false, InteractionStatus.Faulted)]
        [TestCase(true, InteractionStatus.Succeeded)]
        public void ConfirmationHandlerFailureReturnsTheActualTerminalOutcome(bool commitFirst, InteractionStatus expectedStatus)
        {
            using var authority = new InteractionAuthority(9);
            var target = new Target();
            int writes = 0;
            using var lease = authority.RegisterTarget(target, request =>
            {
                if (commitFirst) request.TryCommit(() => writes++);
                throw new InvalidOperationException("Confirmation handler failed.");
            });
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            actor.Receive(Command(actor, lease, 1, 1, InteractionCommandKind.Press));
            actor.Receive(Command(actor, lease, 2, 1, InteractionCommandKind.Release));
            var confirm = Command(actor, lease, 3, 1, InteractionCommandKind.Confirm);
            var receipt = actor.Receive(confirm);
            Assert.That(receipt.Rejection, Is.EqualTo(InteractionCommandRejection.None));
            Assert.That(receipt.Outcome?.Status, Is.EqualTo(expectedStatus));
            Assert.That(actor.Receive(confirm).IsReplay, Is.True);
            Assert.That(writes, Is.EqualTo(commitFirst ? 1 : 0));
        }

        private static InteractionCommand Command(InteractionAuthorityActor actor, InteractionAuthorityTarget target,
            long sequence, long action, InteractionCommandKind kind) => new InteractionCommand(9, actor.Activation, sequence, action, target.Identity, kind);
    }
}
