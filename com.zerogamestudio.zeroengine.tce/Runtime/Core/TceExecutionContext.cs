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
        private bool completed;

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

        public void Commit()
        {
            RequireCurrent();
            if (IsCommitted) return;
            IsCommitted = true;
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
