using System;

namespace ZeroEngine.Multiplayer.Replication
{
    [Serializable]
    public readonly struct NetEntityId : IEquatable<NetEntityId>
    {
        public NetEntityId(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Network entity id must be non-zero.");

            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0;

        public bool Equals(NetEntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NetEntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(NetEntityId left, NetEntityId right) => left.Equals(right);
        public static bool operator !=(NetEntityId left, NetEntityId right) => !left.Equals(right);
    }

    [Serializable]
    public readonly struct NetworkEntityKey : IEquatable<NetworkEntityKey>
    {
        public NetworkEntityKey(NetEntityId entityId, uint generation)
        {
            if (!entityId.IsValid)
                throw new ArgumentException("Network entity id must be valid.", nameof(entityId));

            if (generation == 0u)
                throw new ArgumentOutOfRangeException(nameof(generation), "Network entity generation must be non-zero.");

            EntityId = entityId;
            Generation = generation;
        }

        public NetEntityId EntityId { get; }
        public uint Generation { get; }
        public bool IsValid => EntityId.IsValid && Generation != 0u;

        public bool Equals(NetworkEntityKey other)
        {
            return EntityId == other.EntityId && Generation == other.Generation;
        }

        public override bool Equals(object obj) => obj is NetworkEntityKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (EntityId.GetHashCode() * 397) ^ (int)Generation;
            }
        }

        public override string ToString() => IsValid ? $"{EntityId.Value}:{Generation}" : string.Empty;

        public static bool operator ==(NetworkEntityKey left, NetworkEntityKey right) => left.Equals(right);
        public static bool operator !=(NetworkEntityKey left, NetworkEntityKey right) => !left.Equals(right);
    }

    /// <summary>
    /// Allocates session-local entity ids without deriving them from Unity or transport object ids.
    /// Exhaustion fails instead of wrapping and reusing an id.
    /// </summary>
    public sealed class NetworkEntityIdAllocator
    {
        private ulong _nextValue;

        public NetworkEntityIdAllocator(ulong firstValue = 1ul)
        {
            if (firstValue == 0ul)
                throw new ArgumentOutOfRangeException(nameof(firstValue), "First network entity id must be non-zero.");

            _nextValue = firstValue;
        }

        public bool IsExhausted { get; private set; }

        public NetEntityId Allocate()
        {
            if (IsExhausted)
                throw new InvalidOperationException("Network entity id space is exhausted.");

            ulong allocatedValue = _nextValue;
            if (allocatedValue == ulong.MaxValue)
                IsExhausted = true;
            else
                _nextValue = allocatedValue + 1ul;

            return new NetEntityId(allocatedValue);
        }
    }

    public enum NetworkEntityLifecycleState : byte
    {
        None = 0,
        Active = 1,
        Terminal = 2
    }

    public enum NetworkEntityLifecycleRejectReason : byte
    {
        None = 0,
        InvalidEntity = 1,
        Duplicate = 2,
        TerminalAlreadyAccepted = 3,
        StaleGeneration = 4,
        AmbiguousGeneration = 5,
        DifferentEntity = 6
    }

    /// <summary>
    /// Tracks one logical entity across pooled generations. A terminal event can
    /// arrive before its spawn and permanently suppresses that late spawn. A
    /// a different entity id can replace the slot only with a newer globally
    /// ordered generation, so a delayed pooled message cannot poison reuse.
    /// </summary>
    public sealed class NetworkEntityLifecycleLedger
    {
        private bool _hasEntry;
        private NetEntityId _entityId;
        private uint _generation;
        private NetworkEntityLifecycleState _state;

        public const int MaxTrackedEntities = 1;
        public int TrackedEntityCount => _hasEntry ? 1 : 0;

        public bool TryAcceptSpawn(NetworkEntityKey entity, out NetworkEntityLifecycleRejectReason rejection)
        {
            return TryAccept(entity, NetworkEntityLifecycleState.Active, out rejection);
        }

        public bool TryAcceptTerminal(NetworkEntityKey entity, out NetworkEntityLifecycleRejectReason rejection)
        {
            return TryAccept(entity, NetworkEntityLifecycleState.Terminal, out rejection);
        }

        public bool CanAcceptSpawn(NetworkEntityKey entity, out NetworkEntityLifecycleRejectReason rejection)
        {
            return CanAccept(entity, NetworkEntityLifecycleState.Active, out rejection);
        }

        public bool CanAcceptTerminal(NetworkEntityKey entity, out NetworkEntityLifecycleRejectReason rejection)
        {
            return CanAccept(entity, NetworkEntityLifecycleState.Terminal, out rejection);
        }

        public bool TryGetState(
            NetEntityId entityId,
            out NetworkEntityKey entity,
            out NetworkEntityLifecycleState state)
        {
            if (_hasEntry && entityId.IsValid && entityId == _entityId)
            {
                entity = new NetworkEntityKey(_entityId, _generation);
                state = _state;
                return true;
            }

            entity = default;
            state = NetworkEntityLifecycleState.None;
            return false;
        }

        public void Reset()
        {
            _hasEntry = false;
            _entityId = default;
            _generation = 0u;
            _state = NetworkEntityLifecycleState.None;
        }

        private bool TryAccept(
            NetworkEntityKey entity,
            NetworkEntityLifecycleState incomingState,
            out NetworkEntityLifecycleRejectReason rejection)
        {
            if (!CanAccept(entity, incomingState, out rejection))
                return false;

            if (!_hasEntry)
            {
                _hasEntry = true;
                _entityId = entity.EntityId;
                _generation = entity.Generation;
                _state = incomingState;
                return true;
            }

            if (_generation == entity.Generation)
            {
                _state = NetworkEntityLifecycleState.Terminal;
                return true;
            }

            _entityId = entity.EntityId;
            _generation = entity.Generation;
            _state = incomingState;
            return true;
        }

        private bool CanAccept(
            NetworkEntityKey entity,
            NetworkEntityLifecycleState incomingState,
            out NetworkEntityLifecycleRejectReason rejection)
        {
            if (!entity.IsValid)
            {
                rejection = NetworkEntityLifecycleRejectReason.InvalidEntity;
                return false;
            }

            if (!_hasEntry)
            {
                rejection = NetworkEntityLifecycleRejectReason.None;
                return true;
            }

            if (entity.EntityId != _entityId)
            {
                if (entity.Generation == _generation)
                {
                    rejection = NetworkEntityLifecycleRejectReason.DifferentEntity;
                    return false;
                }
                if (MessageSequenceGate.IsNewer(entity.Generation, _generation))
                {
                    rejection = NetworkEntityLifecycleRejectReason.None;
                    return true;
                }
                if (MessageSequenceGate.IsNewer(_generation, entity.Generation))
                {
                    rejection = NetworkEntityLifecycleRejectReason.StaleGeneration;
                    return false;
                }

                rejection = NetworkEntityLifecycleRejectReason.AmbiguousGeneration;
                return false;
            }

            if (_generation == entity.Generation)
            {
                if (incomingState == NetworkEntityLifecycleState.Active
                    && _state == NetworkEntityLifecycleState.Terminal)
                {
                    rejection = NetworkEntityLifecycleRejectReason.TerminalAlreadyAccepted;
                    return false;
                }

                if (_state == incomingState || _state == NetworkEntityLifecycleState.Terminal)
                {
                    rejection = NetworkEntityLifecycleRejectReason.Duplicate;
                    return false;
                }

                rejection = NetworkEntityLifecycleRejectReason.None;
                return true;
            }

            if (MessageSequenceGate.IsNewer(entity.Generation, _generation))
            {
                rejection = NetworkEntityLifecycleRejectReason.None;
                return true;
            }

            if (MessageSequenceGate.IsNewer(_generation, entity.Generation))
            {
                rejection = NetworkEntityLifecycleRejectReason.StaleGeneration;
                return false;
            }

            rejection = NetworkEntityLifecycleRejectReason.AmbiguousGeneration;
            return false;
        }
    }
}
