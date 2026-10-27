using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>
    /// Consumer-owned attack and pulse identity. Scope belongs to the coordinator instance.
    /// This key does not replace a gameplay damage or collision identity.
    /// </summary>
    public readonly struct FeedbackKey : IEquatable<FeedbackKey>
    {
        public long AttackId { get; }
        public long PulseId { get; }
        /// <summary>Optional contact/lifecycle scope within a shared batch. Zero keeps global attack grouping.</summary>
        public ulong ScopeId { get; }
        public bool IsValid => AttackId > 0 && PulseId >= 0;

        public FeedbackKey(long attackId, long pulseId) : this(attackId, pulseId, 0) { }

        public FeedbackKey(long attackId, long pulseId, ulong scopeId)
        {
            AttackId = attackId;
            PulseId = pulseId;
            ScopeId = scopeId;
        }

        public bool Equals(FeedbackKey other) => AttackId == other.AttackId && PulseId == other.PulseId && ScopeId == other.ScopeId;
        public override bool Equals(object obj) => obj is FeedbackKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (((AttackId.GetHashCode() * 397) ^ PulseId.GetHashCode()) * 397) ^ ScopeId.GetHashCode();
            }
        }

        public static bool operator ==(FeedbackKey left, FeedbackKey right) => left.Equals(right);
        public static bool operator !=(FeedbackKey left, FeedbackKey right) => !left.Equals(right);
    }
}
