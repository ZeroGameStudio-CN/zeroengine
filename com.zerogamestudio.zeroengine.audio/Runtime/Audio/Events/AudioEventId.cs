using System;

namespace ZeroEngine.Audio
{
    public readonly struct AudioEventId : IEquatable<AudioEventId>
    {
        public AudioEventId(string value)
        {
            Value = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Audio event id cannot be empty.", nameof(value))
                : value.Trim();
        }

        public string Value { get; }

        public bool Equals(AudioEventId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AudioEventId other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static implicit operator AudioEventId(string value) => new(value);
    }
}
