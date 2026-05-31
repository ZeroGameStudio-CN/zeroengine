using System;

namespace ZeroEngine.Audio
{
    public readonly struct AudioParameterId : IEquatable<AudioParameterId>
    {
        public AudioParameterId(string value)
        {
            Value = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Audio parameter id cannot be empty.", nameof(value))
                : value.Trim();
        }

        public string Value { get; }

        public bool Equals(AudioParameterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AudioParameterId other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static implicit operator AudioParameterId(string value) => new(value);
    }
}
