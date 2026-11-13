using System;
using System.Collections.Generic;

namespace ZeroEngine.Interaction.Core
{
    /// <summary>Explicit scope; no static registry, input, Unity time, inventory or transport dependency.</summary>
    public sealed class InteractionWorld : IDisposable
    {
        private readonly Dictionary<string, InteractionRegistration> targets = new Dictionary<string, InteractionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, InteractionAgent> actors = new Dictionary<string, InteractionAgent>(StringComparer.Ordinal);
        private long activation;
        public bool IsDisposed { get; private set; }
        public int Count => targets.Count;
        internal event Action<InteractionRegistration> Removed;

        public InteractionRegistration Register(IInteractionTarget target)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(InteractionWorld));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(target.Id)) throw new ArgumentException("A stable target ID is required.", nameof(target));
            if (targets.ContainsKey(target.Id)) throw new InvalidOperationException("Target ID is already registered: " + target.Id);
            var registration = new InteractionRegistration(this, target, checked(++activation));
            targets.Add(registration.Id, registration);
            return registration;
        }

        internal void CopyTargets(List<InteractionRegistration> destination)
        {
            destination.Clear();
            foreach (var value in targets.Values) destination.Add(value);
        }

        internal void Attach(InteractionAgent actor)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(InteractionWorld));
            if (actors.ContainsKey(actor.ActorId)) throw new InvalidOperationException("Actor ID is already registered: " + actor.ActorId);
            actors.Add(actor.ActorId, actor);
        }

        internal void Detach(InteractionAgent actor)
        {
            if (actors.TryGetValue(actor.ActorId, out var current) && ReferenceEquals(current, actor)) actors.Remove(actor.ActorId);
        }

        internal void Remove(InteractionRegistration registration)
        {
            if (!targets.TryGetValue(registration.Id, out var current) || !ReferenceEquals(current, registration)) return;
            targets.Remove(registration.Id);
            Removed?.Invoke(registration);
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            List<Exception> failures = null;
            var snapshot = new List<InteractionAgent>(actors.Values);
            foreach (var actor in snapshot)
            {
                try { actor.Dispose(); }
                catch (Exception exception) { (failures ?? (failures = new List<Exception>())).Add(exception); }
            }
            var registrations = new List<InteractionRegistration>(targets.Values);
            foreach (var registration in registrations)
            {
                try { registration.Dispose(); }
                catch (Exception exception) { (failures ?? (failures = new List<Exception>())).Add(exception); }
            }
            Removed = null;
            if (failures != null) throw new AggregateException("Interaction scope disposed with observer failures.", failures);
        }
    }

    /// <summary>A single activation lease. An old lease cannot remove a pooled target's newer registration.</summary>
    public sealed class InteractionRegistration : IDisposable
    {
        private InteractionWorld world;
        public string Id { get; }
        public long Activation { get; }
        public IInteractionTarget Target { get; }
        public bool IsRegistered => world != null && !world.IsDisposed;
        internal InteractionRegistration(InteractionWorld world, IInteractionTarget target, long activation)
        { this.world = world; Target = target; Id = target.Id; Activation = activation; }
        public void Dispose()
        {
            var previous = world;
            world = null;
            previous?.Remove(this);
        }
    }
}
