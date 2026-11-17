using NUnit.Framework;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer.Tests
{
    public sealed class InteractionExecutionBindingTests
    {
        private sealed class Clock : IInteractionClock { public double Now => 0; }
        private sealed class Range : IInteractionRange
        { public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = 1; return true; } }
        private sealed class ContentTarget : IInteractionTarget
        {
            public string Id { get; set; }
            public int Priority => 0;
            public bool IsAvailable => true;
            public InteractionPolicy Policy => InteractionPolicy.Press;
            public IInteractionExecution Execution;
            public int Starts;
            public InteractionCheck Check(InteractionContext context) => InteractionCheck.Permit;
            public void Execute(InteractionRequest request) { Starts++; Execution = request; }
        }
        private static NetworkEntityKey Key(ulong id) => new NetworkEntityKey(new NetEntityId(id), 1);

        [Test] public void IndependentServerContentCompletesTheOriginalRemoteActionOnce()
        {
            using var world = new InteractionReplicationAuthority(11);
            var target = new ContentTarget { Id = Key(2).ToString() }; using var lease = world.RegisterTarget(Key(2), target);
            using var peer = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var input = new InteractionInputSession(11, peer.Receiver.Activation);
            input.TryPress(lease.Identity, out var press); peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), press));
            input.TryRelease(out var release); peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), release));
            var token = target.Execution.LifetimeToken; int writes = 0;
            Assert.That(target.Execution.TryCommit(() => writes++), Is.True);
            var replay = peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), press));
            Assert.That(replay.IsReplay, Is.True); Assert.That(replay.Outcome.Value.ActionId, Is.EqualTo(press.ActionId));
            Assert.That(replay.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Succeeded));
            Assert.That(token.IsCancellationRequested, Is.True); Assert.That(target.Starts, Is.EqualTo(1)); Assert.That(writes, Is.EqualTo(1));
        }

        [Test] public void RemoteCancelEndsContentAndAReplayedLateCompletionCannotGrant()
        {
            using var world = new InteractionReplicationAuthority(11);
            var target = new ContentTarget { Id = Key(2).ToString() }; using var lease = world.RegisterTarget(Key(2), target);
            using var peer = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var input = new InteractionInputSession(11, peer.Receiver.Activation);
            input.TryPress(lease.Identity, out var press); peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), press));
            var token = target.Execution.LifetimeToken;
            input.TryCancel(out var cancel); peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), cancel));
            peer.Receive(new InteractionNetworkCommand(Key(1), Key(2), cancel));
            Assert.That(target.Execution.TryCommit(), Is.False); Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(target.Execution.Outcome.Value.Status, Is.EqualTo(InteractionStatus.Cancelled)); Assert.That(target.Starts, Is.EqualTo(1));
        }
    }
}
