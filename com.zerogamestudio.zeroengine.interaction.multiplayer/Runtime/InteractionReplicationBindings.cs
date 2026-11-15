using System;
using System.Collections.Generic;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer
{
    /// <summary>The transport supplies the authenticated receiving peer. Payload identities cannot select another actor.</summary>
    public readonly struct InteractionNetworkCommand
    {
        public NetworkEntityKey Actor { get; }
        public NetworkEntityKey Target { get; }
        public InteractionCommand Command { get; }
        public InteractionNetworkCommand(NetworkEntityKey actor, NetworkEntityKey target, InteractionCommand command)
        { Actor = actor; Target = target; Command = command; }
    }

    /// <summary>Reuses ZE entity/generation identities; owns no connection, tick, RPC, wallet or gameplay rules.</summary>
    public sealed class InteractionReplicationAuthority : IDisposable
    {
        private readonly Dictionary<NetworkEntityKey, InteractionReplicationPeer> peers = new Dictionary<NetworkEntityKey, InteractionReplicationPeer>();
        public InteractionAuthority Runtime { get; }
        public InteractionReplicationAuthority(uint epoch) { Runtime = new InteractionAuthority(epoch); }
        public InteractionAuthorityTarget RegisterTarget(NetworkEntityKey entity, IInteractionTarget target, Action<InteractionRequest> confirm = null)
        {
            if (!entity.IsValid || target == null || !StringComparer.Ordinal.Equals(target.Id, entity.ToString()))
                throw new ArgumentException("The target adapter must use its canonical ZE entity/generation identity.");
            return Runtime.RegisterTarget(target, confirm);
        }
        public InteractionReplicationPeer BindPeer(NetworkEntityKey actor, object subject, IInteractionClock clock,
            IInteractionRange authoritativeRange, double maxDistance, int historyLimit = 128)
        {
            if (!actor.IsValid) throw new ArgumentException("Actor identity is invalid.", nameof(actor));
            if (peers.ContainsKey(actor)) throw new InvalidOperationException("Actor already has a receiving binding.");
            var receiver = Runtime.RegisterActor(actor.ToString(), subject, clock, authoritativeRange, maxDistance, historyLimit);
            var peer = new InteractionReplicationPeer(this, actor, receiver);
            peers.Add(actor, peer); return peer;
        }
        internal void Unbind(InteractionReplicationPeer peer)
        {
            if (peers.TryGetValue(peer.Entity, out var current) && ReferenceEquals(peer, current)) peers.Remove(peer.Entity);
        }
        public void Tick() { Runtime.Tick(); }
        public void Dispose()
        {
            List<Exception> failures = null;
            foreach (var peer in new List<InteractionReplicationPeer>(peers.Values))
            { try { peer.Dispose(); } catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); } }
            try { Runtime.Dispose(); } catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
            if (failures != null) throw new AggregateException("Replication bindings disposed with provider cleanup failures.", failures);
        }
    }

    public sealed class InteractionReplicationPeer : IDisposable
    {
        private readonly InteractionReplicationAuthority owner;
        public NetworkEntityKey Entity { get; }
        public InteractionAuthorityActor Receiver { get; }
        public bool IsCurrent => Receiver.IsCurrent;
        internal InteractionReplicationPeer(InteractionReplicationAuthority owner, NetworkEntityKey entity, InteractionAuthorityActor receiver)
        { this.owner = owner; Entity = entity; Receiver = receiver; }
        public InteractionCommandReceipt Receive(InteractionNetworkCommand value)
        {
            if (value.Actor != Entity) return Reject(value.Command, InteractionCommandRejection.UnauthorizedActor);
            if (!value.Target.IsValid || !StringComparer.Ordinal.Equals(value.Target.ToString(), value.Command.Target.Id))
                return Reject(value.Command, InteractionCommandRejection.WrongTarget);
            return Receiver.Receive(value.Command);
        }
        private InteractionCommandReceipt Reject(InteractionCommand command, InteractionCommandRejection reason) =>
            new InteractionCommandReceipt(Receiver.Epoch, Receiver.ActorId, Receiver.Activation, command.Sequence,
                command.ActionId, false, false, false, false, false, reason, null);
        public void Dispose() { owner.Unbind(this); Receiver.Dispose(); }
    }
}
