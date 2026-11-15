using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ZeroEngine.Pool
{
    /// <summary>Owns one exact pool spawn, including a setup phase while inactive.</summary>
    public readonly struct PoolSpawnLease : IDisposable, IEquatable<PoolSpawnLease>
    {
        private readonly PoolManager _owner;
        private readonly GameObject _instance;
        private readonly ulong _version;

        internal PoolSpawnLease(PoolManager owner, GameObject instance, ulong version)
        {
            _owner = owner;
            _instance = instance;
            _version = version;
        }

        public bool IsCurrent => _owner != null && _owner.IsSpawnCurrent(_instance, _version);
        public GameObject Instance => IsCurrent ? _instance : null;

        /// <summary>Returns this spawn once. Stale/default/copied leases never return a later spawn.</summary>
        public bool TryDespawn() => _owner != null && _owner.TryDespawnLease(_instance, _version);
        public void Dispose() => TryDespawn();

        public bool Equals(PoolSpawnLease other) => ReferenceEquals(_owner, other._owner)
            && ReferenceEquals(_instance, other._instance) && _version == other._version;
        public override bool Equals(object obj) => obj is PoolSpawnLease other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int owner = ReferenceEquals(_owner, null) ? 0 : RuntimeHelpers.GetHashCode(_owner);
                int instance = ReferenceEquals(_instance, null) ? 0 : RuntimeHelpers.GetHashCode(_instance);
                return ((owner * 397) ^ instance) * 397 ^ _version.GetHashCode();
            }
        }
        public static bool operator ==(PoolSpawnLease left, PoolSpawnLease right) => left.Equals(right);
        public static bool operator !=(PoolSpawnLease left, PoolSpawnLease right) => !left.Equals(right);
    }
}
