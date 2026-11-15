using NUnit.Framework;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer.Tests
{
    public sealed class InteractionReplicationBindingTests
    {
        private sealed class Clock : IInteractionClock { public double Now => 0; }
        private sealed class Range : IInteractionRange
        { public bool TryGetSquaredDistance(IInteractionTarget target, out double distance) { distance = 1; return true; } }
        private sealed class Target : IInteractionTarget
        {
            public string Id { get; set; }
            public int Priority => 0;
            public bool IsAvailable => true;
            public InteractionPolicy Policy => InteractionPolicy.Press;
            public int Mutations;
            public InteractionCheck Check(InteractionContext context) => InteractionCheck.Permit;
            public void Execute(InteractionRequest request) { request.TryCommit(() => Mutations++); }
        }
        private static NetworkEntityKey Key(ulong id, uint generation = 1) => new NetworkEntityKey(new NetEntityId(id), generation);

        [Test] public void PayloadCannotSelectAnotherPeersAuthorityBinding()
        {
            using var world = new InteractionReplicationAuthority(1);
            var target = new Target { Id = Key(3).ToString() }; using var lease = world.RegisterTarget(Key(3), target);
            using var peer = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var command = new InteractionCommand(1, peer.Receiver.Activation, 1, 1, lease.Identity, InteractionCommandKind.Press);
            Assert.That(peer.Receive(new InteractionNetworkCommand(Key(2), Key(3), command)).Rejection,
                Is.EqualTo(InteractionCommandRejection.UnauthorizedActor));
            Assert.That(target.Mutations, Is.Zero);
            Assert.That(peer.Receive(new InteractionNetworkCommand(Key(1), Key(3), command)).InputAdmitted, Is.True);
            Assert.That(target.Mutations, Is.EqualTo(1));
        }
        [Test] public void WireTargetGenerationMustAgreeWithItsCanonicalTargetIdentity()
        {
            using var world = new InteractionReplicationAuthority(1);
            var target = new Target { Id = Key(3).ToString() }; using var lease = world.RegisterTarget(Key(3), target);
            using var peer = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var command = new InteractionCommand(1, peer.Receiver.Activation, 1, 1, lease.Identity, InteractionCommandKind.Press);
            Assert.That(peer.Receive(new InteractionNetworkCommand(Key(1), Key(3, 2), command)).Rejection,
                Is.EqualTo(InteractionCommandRejection.WrongTarget)); Assert.That(target.Mutations, Is.Zero);
        }
        [Test] public void UnbindingInvalidatesOldReceiversEvenWhenTheSameEntityRebinds()
        {
            using var world = new InteractionReplicationAuthority(1);
            var target = new Target { Id = Key(3).ToString() }; using var lease = world.RegisterTarget(Key(3), target);
            var old = world.BindPeer(Key(1), null, new Clock(), new Range(), 2); old.Dispose();
            using var current = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var oldCommand = new InteractionCommand(1, old.Receiver.Activation, 1, 1, lease.Identity, InteractionCommandKind.Press);
            Assert.That(current.Receive(new InteractionNetworkCommand(Key(1), Key(3), oldCommand)).Rejection,
                Is.EqualTo(InteractionCommandRejection.WrongActorActivation)); Assert.That(target.Mutations, Is.Zero);
        }
        [Test] public void NoNetworkingSdkIsNeededForAnOfflineAuthorityReceiver()
        {
            using var world = new InteractionReplicationAuthority(1);
            var target = new Target { Id = Key(3).ToString() }; using var lease = world.RegisterTarget(Key(3), target);
            using var peer = world.BindPeer(Key(1), null, new Clock(), new Range(), 2);
            var input = new InteractionInputSession(1, peer.Receiver.Activation);
            Assert.That(input.TryPress(lease.Identity, out var command), Is.True);
            peer.Receive(new InteractionNetworkCommand(Key(1), Key(3), command));
            peer.Receive(new InteractionNetworkCommand(Key(1), Key(3), command));
            Assert.That(target.Mutations, Is.EqualTo(1));
        }
    }
}
