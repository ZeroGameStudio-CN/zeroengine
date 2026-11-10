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
        public bool IsValid => AttackId > 0 && PulseId >= 0;

        public FeedbackKey(long attackId, long pulseId)
        {
            AttackId = attackId;
            PulseId = pulseId;
        }

        public bool Equals(FeedbackKey other) => AttackId == other.AttackId && PulseId == other.PulseId;
        public override bool Equals(object obj) => obj is FeedbackKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (AttackId.GetHashCode() * 397) ^ PulseId.GetHashCode();
            }
        }

        public static bool operator ==(FeedbackKey left, FeedbackKey right) => left.Equals(right);
        public static bool operator !=(FeedbackKey left, FeedbackKey right) => !left.Equals(right);
    }
}
