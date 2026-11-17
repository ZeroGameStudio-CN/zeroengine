using System;
using System.Text.Json;
using IndependentContent.Example;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Interaction.Samples.ExternalModule;

internal static class Program
{
    private sealed class Clock : IInteractionClock { public double Now => 0; }
    private sealed class Range : IInteractionRange
    { public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = 1; return true; } }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static void Main()
    {
        foreach (var reference in typeof(IndependentModule).Assembly.GetReferencedAssemblies())
            Require(!reference.Name.StartsWith("ZeroEngine", StringComparison.Ordinal), "Independent module depends on ZE.");
        foreach (string scenario in new[] { "complete", "cancel-late", "revoked", "decline", "replaced" })
        {
            int writes = 0; bool allowed = true;
            var adapter = new ModuleInteractionAdapter("external", context => allowed ? InteractionCheck.Permit : InteractionCheck.Deny("Context ended."),
                context => { Require(context.ActorId == "actor", "Transaction received the wrong actor."); writes++; return InteractionCheck.Permit; });
            using var authority = new InteractionAuthority(1); using var target = authority.RegisterTarget(adapter);
            using var actor = authority.RegisterActor("actor", null, new Clock(), new Range(), 2);
            void Send(long sequence, long action, InteractionCommandKind kind, InteractionAuthorityTarget lease) =>
                actor.Receive(new InteractionCommand(1, actor.Activation, sequence, action, lease.Identity, kind));
            Send(1, 1, InteractionCommandKind.Press, target); Send(2, 1, InteractionCommandKind.Release, target);
            var original = adapter.Execution; var module = adapter.Module; var late = module.CaptureCompletion();
            var token = original.LifetimeToken;
            if (scenario == "complete") { module.Finish(); module.Finish(); late(true); Require(writes == 1, "Duplicate completion wrote twice."); }
            else if (scenario == "cancel-late") { Send(3, 1, InteractionCommandKind.Cancel, target); late(true); Require(writes == 0, "Late callback survived cancel."); }
            else if (scenario == "revoked") { allowed = false; actor.Tick(); late(true); Require(writes == 0, "Revoked context committed."); }
            else if (scenario == "decline") { module.Finish(false); late(true); Require(writes == 0, "Declined content committed."); }
            else
            {
                target.Dispose(); using var replacement = authority.RegisterTarget(adapter);
                Send(3, 3, InteractionCommandKind.Press, replacement); Send(4, 3, InteractionCommandKind.Release, replacement);
                late(true); Require(writes == 0 && adapter.Module.IsRunning, "Old callback affected the new execution.");
                adapter.Module.Finish(); Require(writes == 1, "Replacement did not finish.");
            }
            Require(token.IsCancellationRequested && !module.IsRunning && module.StopCount == 1, "Independent module lifetime leaked.");
            Require(actor.TryGetOutcome(1, out var outcome), "Authority did not record the execution outcome.");
            Console.WriteLine(JsonSerializer.Serialize(new { scenario, passed = true, writes, terminal = outcome.Status.ToString() }));
        }
    }
}
