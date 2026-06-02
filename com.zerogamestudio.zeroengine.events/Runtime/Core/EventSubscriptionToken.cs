using System;

namespace ZeroEngine.Events
{
    public readonly struct EventSubscriptionToken : IEquatable<EventSubscriptionToken>, IDisposable
    {
        private readonly Action<EventSubscriptionToken> _dispose;

        internal EventSubscriptionToken(Guid id, Type eventType, object owner, Action<EventSubscriptionToken> dispose)
        {
            Id = id;
            EventType = eventType;
            Owner = owner;
            _dispose = dispose;
        }

        public Guid Id { get; }
        public Type EventType { get; }
        public object Owner { get; }
        public bool IsValid => Id != Guid.Empty && EventType != null;

        public void Dispose()
        {
            _dispose?.Invoke(this);
        }

        public bool Equals(EventSubscriptionToken other)
        {
            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            return obj is EventSubscriptionToken other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
