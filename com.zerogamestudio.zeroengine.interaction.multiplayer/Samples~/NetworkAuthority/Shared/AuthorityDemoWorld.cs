using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using ZeroEngine.Interaction.Core;
using ZeroEngine.Interaction.Core.Authority;
using ZeroEngine.Multiplayer.Replication;

namespace ZeroEngine.Interaction.Multiplayer.Samples
{
    [DataContract] public sealed class DemoActorView
    {
        [DataMember] public string Name;
        [DataMember] public ulong Entity;
        [DataMember] public uint Generation;
        [DataMember] public long Binding;
        [DataMember] public int Coins, Reserved, Gifts, Conversations, Purchases;
        [DataMember] public bool Connected;
        [DataMember] public double Position, HoldProgress;
    }
    [DataContract] public sealed class DemoTargetView
    {
        [DataMember] public string Name;
        [DataMember] public ulong Entity;
        [DataMember] public uint Generation;
        [DataMember] public long Activation;
        [DataMember] public bool Available;
    }
    [DataContract] public sealed class DemoWorldView
    {
        [DataMember] public uint Epoch, Revision;
        [DataMember] public bool DoorOpen;
        [DataMember] public int SharedPickups, PersonalPickups, Commits, Reservations, Refunds;
        [DataMember] public string SharedWinner;
        [DataMember] public DemoActorView[] Actors;
        [DataMember] public DemoTargetView[] Targets;
    }

    /// <summary>Only this authority-owned model changes gameplay. It is shared by the Unity scene and the two-process example.</summary>
    public sealed class AuthorityDemoWorld : IDisposable, IInteractionClock
    {
        internal sealed class Actor
        {
            internal string Name;
            internal NetworkEntityKey Entity;
            internal InteractionReplicationPeer Peer;
            internal int Coins, Reserved, Gifts, Conversations, Purchases;
            internal double Position = -10;
            internal bool Connected = true;
            internal Reservation Reservation;
        }
        internal sealed class Reservation
        {
            private readonly AuthorityDemoWorld world;
            private readonly Actor actor;
            private bool settled;
            internal Reservation(AuthorityDemoWorld world, Actor actor)
            { this.world = world; this.actor = actor; actor.Coins -= 2; actor.Reserved += 2; world.Reservations++; }
            internal void Commit()
            {
                if (settled) throw new InvalidOperationException("Reservation already settled.");
                settled = true; actor.Reserved -= 2; actor.Purchases++; actor.Reservation = null; world.Commits++;
            }
            internal void Cancel()
            {
                if (settled) return;
                settled = true; actor.Reserved -= 2; actor.Coins += 2; actor.Reservation = null; world.Refunds++;
            }
        }
        private sealed class Range : IInteractionRange
        {
            private readonly AuthorityDemoWorld world;
            private readonly Actor actor;
            internal Range(AuthorityDemoWorld world, Actor actor) { this.world = world; this.actor = actor; }
            public bool TryGetSquaredDistance(IInteractionTarget target, out double distance)
            {
                distance = 0;
                if (!world.byId.TryGetValue(target.Id, out var item)) return false;
                double delta = actor.Position - item.Position; distance = delta * delta; return true;
            }
        }
        private sealed class Target : IInteractionTarget
        {
            internal readonly AuthorityDemoWorld World;
            internal readonly string Name;
            internal readonly double Position;
            internal NetworkEntityKey Entity;
            internal InteractionAuthorityTarget Lease;
            internal bool Available = true;
            internal Target(AuthorityDemoWorld world, string name, ulong entity, double position)
            { World = world; Name = name; Entity = new NetworkEntityKey(new NetEntityId(entity), 1); Position = position; }
            public string Id => Entity.ToString();
            public int Priority => 0;
            public bool IsAvailable => Available;
            public InteractionPolicy Policy => Name == "npc" ? new InteractionPolicy(InteractionMode.TapOrHold, .7, .4) : InteractionPolicy.Press;
            public InteractionCheck Check(InteractionContext context)
            {
                var actor = World.actors[context.ActorId];
                if (Name == "personal" && actor.Entity != World.host.Entity) return InteractionCheck.Deny("This pickup belongs to the host actor.");
                if (Name == "shop" && actor.Reservation == null && actor.Coins < 2) return InteractionCheck.Deny("Two coins required.");
                if (Name == "npc" && context.Gesture != InteractionGesture.Tap && actor.Coins < 1) return InteractionCheck.Deny("One coin required.");
                return InteractionCheck.Permit;
            }
            public void Execute(InteractionRequest request)
            {
                var actor = World.actors[request.Context.ActorId];
                if (Name == "shop")
                {
                    if (!request.IsCurrent || actor.Reservation != null) { request.TryDeny("Reservation unavailable."); return; }
                    var reservation = new Reservation(World, actor); actor.Reservation = reservation;
                    request.Cancelled += _ => reservation.Cancel(); return;
                }
                request.TryCommit(() =>
                {
                    if (Name == "door") World.DoorOpen = !World.DoorOpen;
                    else if (Name == "npc")
                    {
                        if (request.Context.Gesture == InteractionGesture.Tap) actor.Conversations++;
                        else { actor.Coins--; actor.Gifts++; }
                    }
                    else if (Name == "shared") { Available = false; World.SharedPickups++; World.SharedWinner = actor.Name; }
                    else if (Name == "personal") { Available = false; World.PersonalPickups++; }
                    World.Commits++;
                });
            }
            internal void Confirm(InteractionRequest request)
            {
                var reservation = World.actors[request.Context.ActorId].Reservation;
                if (reservation == null) { request.TryDeny("Reservation ended."); return; }
                if (!request.TryCommit(reservation.Commit)) reservation.Cancel();
            }
        }
        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
        private readonly Dictionary<string, Target> targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        private readonly Dictionary<string, Target> byId = new Dictionary<string, Target>(StringComparer.Ordinal);
        private readonly NetworkEntityIdAllocator actorIds = new NetworkEntityIdAllocator(1000);
        private readonly InteractionReplicationAuthority authority;
        private readonly Actor host;
        private uint revision;
        public uint Epoch => authority.Runtime.Epoch;
        public double Now { get; private set; }
        public bool Paused { get; set; }
        public bool DoorOpen { get; private set; }
        public int SharedPickups { get; private set; }
        public int PersonalPickups { get; private set; }
        public int Commits { get; private set; }
        public int Reservations { get; private set; }
        public int Refunds { get; private set; }
        public string SharedWinner { get; private set; }
        public NetworkEntityKey HostEntity => host.Entity;

        public AuthorityDemoWorld(uint epoch)
        {
            authority = new InteractionReplicationAuthority(epoch);
            AddTarget("door", 1, 0); AddTarget("npc", 2, 5); AddTarget("shared", 3, 10);
            AddTarget("shop", 4, 15); AddTarget("personal", 5, 20);
            host = CreateActor("host", 5);
        }
        private void AddTarget(string name, ulong id, double position)
        {
            var target = new Target(this, name, id, position);
            target.Lease = authority.RegisterTarget(target.Entity, target, name == "shop" ? target.Confirm : null);
            targets.Add(name, target); byId.Add(target.Id, target);
        }
        private Actor CreateActor(string name, int coins)
        {
            var actor = new Actor { Name = name, Coins = coins, Entity = new NetworkEntityKey(actorIds.Allocate(), 1) };
            actor.Peer = authority.BindPeer(actor.Entity, actor, this, new Range(this, actor), 2);
            actors.Add(actor.Entity.ToString(), actor); return actor;
        }
        public NetworkEntityKey ConnectGuest(string name) => CreateActor(name, 3).Entity;
        public InteractionReplicationPeer GetPeer(NetworkEntityKey entity) => actors[entity.ToString()].Peer;
        public void Disconnect(NetworkEntityKey entity)
        {
            var actor = actors[entity.ToString()]; if (!actor.Connected) return;
            actor.Connected = false; actor.Peer.Dispose();
        }
        public void Approach(NetworkEntityKey actor, string target)
        { actors[actor.ToString()].Position = targets[target].Position; }
        public void LeaveRange(NetworkEntityKey actor) { actors[actor.ToString()].Position = -10; }
        public void ReuseShop()
        {
            var target = targets["shop"]; target.Lease.Dispose(); byId.Remove(target.Id);
            target.Entity = new NetworkEntityKey(target.Entity.EntityId, checked(target.Entity.Generation + 1));
            target.Lease = authority.RegisterTarget(target.Entity, target, target.Confirm); byId.Add(target.Id, target);
        }
        public void Tick(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (!Paused) Now += delta;
            authority.Tick();
        }
        public DemoWorldView Snapshot()
        {
            return new DemoWorldView
            {
                Epoch = Epoch, Revision = checked(++revision), DoorOpen = DoorOpen, SharedPickups = SharedPickups,
                PersonalPickups = PersonalPickups, Commits = Commits, Reservations = Reservations, Refunds = Refunds, SharedWinner = SharedWinner,
                Actors = actors.Values.Select(a => new DemoActorView { Name = a.Name, Entity = a.Entity.EntityId.Value,
                    Generation = a.Entity.Generation, Binding = a.Peer.Receiver.Activation, Coins = a.Coins, Reserved = a.Reserved,
                    Gifts = a.Gifts, Conversations = a.Conversations, Purchases = a.Purchases, Connected = a.Connected,
                    Position = a.Position, HoldProgress = a.Peer.Receiver.HoldProgress }).ToArray(),
                Targets = targets.Values.Select(t => new DemoTargetView { Name = t.Name, Entity = t.Entity.EntityId.Value,
                    Generation = t.Entity.Generation, Activation = t.Lease.Identity.Activation, Available = t.Available }).ToArray()
            };
        }
        public void Dispose() { authority.Dispose(); }
    }
}
