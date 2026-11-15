using System;
using System.Collections.Generic;

namespace ZeroEngine.Interaction.Core.Authority
{
    /// <summary>
    /// Single gameplay-thread authority. Offline calls and a transport's authenticated actor binding
    /// use the same receiver. The existing InteractionAgent owns all gesture and commit semantics.
    /// </summary>
    public sealed class InteractionAuthority : IDisposable
    {
        private readonly InteractionWorld world = new InteractionWorld();
        private readonly Dictionary<string, InteractionAuthorityTarget> targets = new Dictionary<string, InteractionAuthorityTarget>(StringComparer.Ordinal);
        private readonly Dictionary<string, InteractionAuthorityActor> actors = new Dictionary<string, InteractionAuthorityActor>(StringComparer.Ordinal);
        private readonly List<InteractionAuthorityActor> tickActors = new List<InteractionAuthorityActor>();
        private long actorActivation;
        private bool ticking;
        public uint Epoch { get; }
        public bool IsDisposed { get; private set; }
        public int ActorCount => actors.Count;
        public int TargetCount => targets.Count;

        public InteractionAuthority(uint epoch)
        { if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch)); Epoch = epoch; }

        public InteractionAuthorityTarget RegisterTarget(IInteractionTarget target, Action<InteractionRequest> confirm = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(InteractionAuthority));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Id == null || target.Id.Length > 512) throw new ArgumentException("Use a bounded target ID.", nameof(target));
            var lease = new InteractionAuthorityTarget(this, world.Register(target), confirm);
            targets.Add(lease.Identity.Id, lease);
            return lease;
        }

        public InteractionAuthorityActor RegisterActor(string actorId, object subject, IInteractionClock clock,
            IInteractionRange range, double maxDistance, int historyLimit = 128)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(InteractionAuthority));
            if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 512) throw new ArgumentException("Use a bounded actor ID.", nameof(actorId));
            if (actors.ContainsKey(actorId)) throw new InvalidOperationException("Actor is already bound: " + actorId);
            if (historyLimit < 2 || historyLimit > 4096) throw new ArgumentOutOfRangeException(nameof(historyLimit));
            var actor = new InteractionAuthorityActor(this, world, actorId, checked(++actorActivation), subject,
                clock, range, maxDistance, historyLimit);
            actors.Add(actorId, actor);
            return actor;
        }

        internal bool IsCurrent(InteractionAuthorityActor actor) => !IsDisposed &&
            actors.TryGetValue(actor.ActorId, out var current) && ReferenceEquals(actor, current);
        internal bool TryGetTarget(InteractionTargetIdentity identity, out InteractionAuthorityTarget target)
        {
            if (!IsDisposed && targets.TryGetValue(identity.Id ?? string.Empty, out target) &&
                target.Identity.Equals(identity) && target.IsCurrent) return true;
            target = null; return false;
        }
        internal void Remove(InteractionAuthorityTarget target)
        {
            if (targets.TryGetValue(target.Identity.Id, out var current) && ReferenceEquals(current, target)) targets.Remove(target.Identity.Id);
        }
        internal void Remove(InteractionAuthorityActor actor)
        {
            if (actors.TryGetValue(actor.ActorId, out var current) && ReferenceEquals(current, actor)) actors.Remove(actor.ActorId);
        }

        public void Tick()
        {
            if (IsDisposed) return;
            if (ticking) throw new InvalidOperationException("Authority Tick cannot reenter.");
            ticking = true;
            try
            {
                tickActors.Clear(); tickActors.AddRange(actors.Values);
                foreach (var actor in tickActors) actor.Tick();
            }
            finally { tickActors.Clear(); ticking = false; }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            List<Exception> failures = null;
            foreach (var actor in new List<InteractionAuthorityActor>(actors.Values))
            { try { actor.Dispose(); } catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); } }
            foreach (var target in new List<InteractionAuthorityTarget>(targets.Values))
            { try { target.Dispose(); } catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); } }
            try { world.Dispose(); } catch (Exception error) { (failures ?? (failures = new List<Exception>())).Add(error); }
            if (failures != null) throw new AggregateException("Authority disposed with provider cleanup failures.", failures);
        }
    }

    public sealed class InteractionAuthorityTarget : IDisposable
    {
        private readonly InteractionAuthority owner;
        internal InteractionRegistration Registration { get; }
        internal Action<InteractionRequest> Confirm { get; }
        public InteractionTargetIdentity Identity { get; }
        public bool IsCurrent => !owner.IsDisposed && Registration.IsRegistered;
        internal InteractionAuthorityTarget(InteractionAuthority owner, InteractionRegistration registration, Action<InteractionRequest> confirm)
        { this.owner = owner; Registration = registration; Confirm = confirm; Identity = new InteractionTargetIdentity(registration.Id, registration.Activation); }
        public void Dispose() { owner.Remove(this); Registration.Dispose(); }
    }
}
