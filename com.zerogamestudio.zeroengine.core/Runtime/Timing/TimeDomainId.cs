using System;

namespace ZeroEngine.Timing
{
    public readonly struct TimeDomainId : IEquatable<TimeDomainId>
    {
        private readonly string _value;

        public TimeDomainId(string value)
        {
            _value = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public override string ToString()
        {
            return Value;
        }

        public bool Equals(TimeDomainId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is TimeDomainId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public static bool operator ==(TimeDomainId left, TimeDomainId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TimeDomainId left, TimeDomainId right)
        {
            return !left.Equals(right);
        }
    }
}
