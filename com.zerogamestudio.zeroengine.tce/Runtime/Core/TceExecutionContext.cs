using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ZeroEngine.TCE
{
    /// <summary>The adapter owns activation and world identity; a CLR reference is not an activation.</summary>
    public interface ITceObjectLifetime
    {
        object Identity { get; }
        object NativeObject { get; }
        long Activation { get; }
        long World { get; }
        bool IsValid { get; }
        bool IsActive { get; }
    }

    public readonly struct TceObjectRef : IEquatable<TceObjectRef>
    {
        private readonly ITceObjectLifetime lifetime;
        private readonly object identity;
        public readonly long Activation;
        public readonly long World;

        public TceObjectRef(ITceObjectLifetime lifetime)
        {
            this.lifetime = lifetime;
            identity = lifetime?.Identity;
            Activation = lifetime?.Activation ?? 0;
            World = lifetime?.World ?? 0;
        }

        public bool IsAssigned => !ReferenceEquals(identity, null);
        public bool IsCurrent => IsAssigned && lifetime.IsValid &&
            lifetime.Activation == Activation && lifetime.World == World;
        public bool IsActive => IsCurrent && lifetime.IsActive;

        public bool TryResolve<T>(out T value) where T : class
        {
            value = IsCurrent ? lifetime.NativeObject as T : null;
            return value != null;
        }

        public bool Equals(TceObjectRef other) => ReferenceEquals(identity, other.identity) &&
            Activation == other.Activation && World == other.World;
        public override bool Equals(object other) => other is TceObjectRef reference && Equals(reference);
        public override int GetHashCode() => HashCode.Combine(
            identity == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(identity), Activation, World);
    }

    /// <summary>Stable binding of one installed ability. It is never used as an event payload.</summary>
    public sealed class TceInstallationContext
    {
        public TceInstallationContext(object definition, TceObjectRef host, TceObjectRef grantor,
            ITceClock clock, object source, object instanceKey)
        {
            Definition = definition;
            Host = host;
            Grantor = grantor;
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Source = source;
            InstanceKey = instanceKey ?? throw new ArgumentNullException(nameof(instanceKey));
        }

        public object Definition { get; }
        public TceObjectRef Host { get; }
        public TceObjectRef Grantor { get; }
        public ITceClock Clock { get; }
        public object Source { get; }
        public object InstanceKey { get; }
    }

    /// <summary>Immutable event facts. Live write capabilities belong to the owning adapter's synchronous window.</summary>
    public sealed class TceEventContext
    {
        private static long nextId;

        public TceEventContext(TceObjectRef subject, TceObjectRef target, TceObjectRef instigator,
            TceObjectRef emitter, object payload = null, Vector3? position = null, Vector2? direction = null,
            TceEventContext cause = null)
        {
            Id = Interlocked.Increment(ref nextId);
            RootId = cause?.RootId ?? Id;
            CauseId = cause?.Id ?? 0;
            Subject = subject;
            Target = target;
            Instigator = instigator;
            Emitter = emitter;
            Payload = payload;
            Position = position;
            Direction = direction;
        }

        public long Id { get; }
        public long RootId { get; }
        public long CauseId { get; }
        public TceObjectRef Subject { get; }
        public TceObjectRef Target { get; }
        public TceObjectRef Instigator { get; }
        public TceObjectRef Emitter { get; }
        public object Payload { get; }
        public Vector3? Position { get; }
        public Vector2? Direction { get; }
        public bool TryGetPayload<T>(out T payload)
        {
            if (Payload is T typed) { payload = typed; return true; }
            payload = default;
            return false;
        }
    }

    /// <summary>One execution owns its cancellation, identity and transient contributions, including across awaits.</summary>
    public sealed class TceExecutionContext
    {
        private static long nextId;
        private readonly Func<bool> isCurrent;
        private Dictionary<Type, object> values;
        private List<Action> commitNotifications;
        private Dictionary<(TceObjectRef Owner, string Key), ResourceCost> resourceCosts;
        private bool committing;
        private bool completed;

        private sealed class ResourceCost
        {
            public TceObjectRef Owner;
            public int Amount;
            public Func<int> Available;
            public Action<int, Action<Action>> Consume;
        }

        public TceExecutionContext(TceInstallationContext installation, TceEventContext eventContext,
            Func<bool> isCurrent = null, long parentExecutionId = 0)
        {
            Installation = installation ?? throw new ArgumentNullException(nameof(installation));
            Event = eventContext ?? throw new ArgumentNullException(nameof(eventContext));
            this.isCurrent = isCurrent;
            ParentExecutionId = parentExecutionId;
            Id = Interlocked.Increment(ref nextId);
        }

        public long Id { get; }
        public long ParentExecutionId { get; }
        public TceInstallationContext Installation { get; }
        public TceEventContext Event { get; }
        public bool IsCurrent => !completed && (isCurrent?.Invoke() ?? true);
        public bool IsCommitted { get; private set; }
        public void DeferCommitNotification(Action notification)
        {
            RequireCurrent();
            if (notification == null) return;
            if (IsCommitted) notification();
            else (commitNotifications ??= new List<Action>()).Add(notification);
        }

        /// <summary>
        /// Reserves a cost without changing its resource. Equal owner/key pairs share one total.
        /// Availability reads must be pure. Consume must be synchronous, non-rejecting after validation,
        /// and defer observable callbacks through the supplied notification sink.
        /// </summary>
        public bool TryReserveResource(TceObjectRef owner, string key, int amount,
            Func<int> available, Action<int, Action<Action>> consume)
        {
            RequireCurrent();
            if (IsCommitted || committing) throw new InvalidOperationException("Admission has already ended.");
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A resource identity is required.", nameof(key));
            if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount));
            if (available == null) throw new ArgumentNullException(nameof(available));
            if (consume == null) throw new ArgumentNullException(nameof(consume));
            if (!owner.IsCurrent) return false;
            var identity = (owner, key);
            resourceCosts ??= new Dictionary<(TceObjectRef, string), ResourceCost>();
            resourceCosts.TryGetValue(identity, out var cost);
            long total = (long)(cost?.Amount ?? 0) + amount;
            if (total > int.MaxValue || (cost?.Available ?? available)() < total) return false;
            if (cost == null)
                resourceCosts.Add(identity, cost = new ResourceCost { Owner = owner, Available = available, Consume = consume });
            cost.Amount = (int)total;
            return true;
        }

        /// <summary>Revalidates all costs before any mutation, then commits state before publishing callbacks.</summary>
        public bool TryCommit(Action commitState = null)
        {
            RequireCurrent();
            if (IsCommitted) return true;
            if (committing) throw new InvalidOperationException("A commit cannot reenter its own admission.");
            committing = true;
            try
            {
                if (resourceCosts != null)
                {
                    foreach (var cost in resourceCosts.Values)
                        if (!cost.Owner.IsCurrent || cost.Available() < cost.Amount) return false;
                    foreach (var cost in resourceCosts.Values)
                        cost.Consume(cost.Amount, DeferCommitNotification);
                }
                commitState?.Invoke();
                IsCommitted = true;
                resourceCosts?.Clear();
                PublishCommitNotifications();
                return true;
            }
            finally { committing = false; }
        }

        public void Commit()
        {
            if (!TryCommit()) throw new InvalidOperationException("An admission resource is no longer available.");
        }

        private void PublishCommitNotifications()
        {
            var pending = commitNotifications;
            commitNotifications = null;
            if (pending == null) return;
            List<Exception> errors = null;
            foreach (var notification in pending)
                try { notification(); }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            if (errors != null) throw new AggregateException(errors);
        }

        public void Complete()
        {
            completed = true;
            values?.Clear();
            commitNotifications?.Clear();
            resourceCosts?.Clear();
        }

        private void RequireCurrent()
        {
            if (!IsCurrent) throw new InvalidOperationException("The ability execution is no longer current.");
        }
        public void Set<T>(T value) where T : class
        {
            RequireCurrent();
            (values ??= new Dictionary<Type, object>())[typeof(T)] = value;
        }
        public bool TryGet<T>(out T value) where T : class
        {
            value = values != null && values.TryGetValue(typeof(T), out var found) ? found as T : null;
            return value != null;
        }
        public bool TryTake<T>(out T value) where T : class
        {
            if (!IsCurrent) { value = null; return false; }
            if (!TryGet(out value)) return false;
            values.Remove(typeof(T));
            return true;
        }
    }
}
