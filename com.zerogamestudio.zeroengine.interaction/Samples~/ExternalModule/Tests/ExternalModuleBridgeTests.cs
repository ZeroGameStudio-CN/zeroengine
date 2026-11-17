using System;
using System.Linq;
using IndependentContent.Example;
using NUnit.Framework;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;

namespace ZeroEngine.Interaction.Samples.ExternalModule.Tests
{
    public sealed class ExternalModuleBridgeTests
    {
        private sealed class Clock : IInteractionClock { public double Now => 0; }
        private sealed class Range : IInteractionRange
        { public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = 1; return true; } }

        [Test] public void IndependentAssemblyCompletesThroughAThinAuthorityAdapter()
        {
            var dependencies = typeof(IndependentModule).Assembly.GetReferencedAssemblies();
            Assert.That(dependencies.Any(a => a.Name.StartsWith("ZeroEngine", StringComparison.Ordinal) ||
                a.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
            int writes = 0; string committedActor = null;
            var target = new ModuleInteractionAdapter("external", _ => InteractionCheck.Permit,
                context => { writes++; committedActor = context.ActorId; return InteractionCheck.Permit; });
            using var authority = new InteractionAuthority(1); using var lease = authority.RegisterTarget(target);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            Send(actor, lease, 1, 1, InteractionCommandKind.Press); Send(actor, lease, 2, 1, InteractionCommandKind.Release);
            var module = target.Module; var token = target.Execution.LifetimeToken;
            module.Finish(); module.Finish();
            Assert.That(writes, Is.EqualTo(1)); Assert.That(committedActor, Is.EqualTo("actor"));
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(module.StopCount, Is.EqualTo(1));
            Assert.That(target.Execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
        }

        [Test] public void ACancelledModulesCapturedCallbackCannotFinishItsReplacement()
        {
            int writes = 0;
            var target = new ModuleInteractionAdapter("external", _ => InteractionCheck.Permit,
                _ => { writes++; return InteractionCheck.Permit; });
            using var authority = new InteractionAuthority(1); using var lease = authority.RegisterTarget(target);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            Send(actor, lease, 1, 1, InteractionCommandKind.Press); Send(actor, lease, 2, 1, InteractionCommandKind.Release);
            var oldModule = target.Module; var oldExecution = target.Execution; var late = oldModule.CaptureCompletion();
            Send(actor, lease, 3, 1, InteractionCommandKind.Cancel);
            Send(actor, lease, 4, 4, InteractionCommandKind.Press); Send(actor, lease, 5, 4, InteractionCommandKind.Release);
            late(true);
            Assert.That(writes, Is.Zero); Assert.That(target.Module.IsRunning, Is.True);
            Assert.That(oldModule.StopCount, Is.EqualTo(1)); Assert.That(oldExecution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled));
            target.Module.Finish(); Assert.That(writes, Is.EqualTo(1));
            Assert.That(actor.TryGetOutcome(4, out var result), Is.True); Assert.That(result.Status, Is.EqualTo(InteractionStatus.Succeeded));
        }

        private static void Send(InteractionAuthorityActor actor, InteractionAuthorityTarget target, long sequence, long action, InteractionCommandKind kind) =>
            actor.Receive(new InteractionCommand(1, actor.Activation, sequence, action, target.Identity, kind));
    }
}
