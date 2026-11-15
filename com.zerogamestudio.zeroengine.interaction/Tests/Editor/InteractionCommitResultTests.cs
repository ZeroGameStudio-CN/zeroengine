using System;
using NUnit.Framework;
using ZeroEngine.Interaction.Core;

namespace ZeroEngine.Interaction.Tests
{
    public sealed class InteractionCommitResultTests
    {
        private sealed class Clock : IInteractionClock { public double Now => 0; }
        private sealed class Range : IInteractionRange
        { public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = 0; return true; } }
        private sealed class Target : IInteractionTarget
        {
            public string Id => "target";
            public int Priority => 0;
            public bool IsAvailable => true;
            public InteractionPolicy Policy => InteractionPolicy.Press;
            public Action<InteractionRequest> Handler;
            public InteractionCheck Check(InteractionContext context) => InteractionCheck.Permit;
            public void Execute(InteractionRequest request) { Handler(request); }
        }
        [Test] public void ExplicitBusinessRejectionIsDeniedAndCannotExecuteAgain()
        {
            using var world = new InteractionWorld(); var target = new Target(); using var lease = world.Register(target);
            using var agent = new InteractionAgent(world, "actor", null, new Clock(), new Range(), 1);
            InteractionResult result = default; agent.Completed += value => result = value;
            int attempts = 0; InteractionRequest retained = null;
            target.Handler = request => { retained = request; request.TryCommitResult(() => { attempts++; return InteractionCheck.Deny("Grant rejected."); }); };
            agent.Press(); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Denied));
            Assert.That(result.Reason, Is.EqualTo("Grant rejected."));
            Assert.That(retained.TryCommitResult(() => { attempts++; return InteractionCheck.Permit; }), Is.False);
            Assert.That(attempts, Is.EqualTo(1));
        }
        [Test] public void ExistingAssignmentLambdaReturningFalseStillReportsActionSuccess()
        {
            using var world = new InteractionWorld(); var target = new Target(); using var lease = world.Register(target);
            using var agent = new InteractionAgent(world, "actor", null, new Clock(), new Range(), 1);
            bool opened = true; InteractionResult result = default; agent.Completed += value => result = value;
            target.Handler = request => request.TryCommit(() => opened = !opened);
            agent.Press(); Assert.That(opened, Is.False); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Succeeded));
        }
        [Test] public void ExplicitResultFaultIsSettledAndNeverRetriesAPartialCallback()
        {
            using var world = new InteractionWorld(); var target = new Target(); using var lease = world.Register(target);
            using var agent = new InteractionAgent(world, "actor", null, new Clock(), new Range(), 1);
            int attempts = 0; InteractionRequest retained = null; InteractionResult result = default;
            agent.Completed += value => result = value;
            target.Handler = request => { retained = request; request.TryCommitResult(() => { attempts++; throw new InvalidOperationException("provider fault"); }); };
            agent.Press(); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Faulted));
            Assert.That(retained.TryCommitResult(() => { attempts++; return InteractionCheck.Permit; }), Is.False);
            Assert.That(attempts, Is.EqualTo(1));
        }
    }
}
