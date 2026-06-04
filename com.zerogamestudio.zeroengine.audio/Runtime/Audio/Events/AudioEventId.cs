using System;

namespace ZeroEngine.Audio.Events
{
    [Serializable]
    public readonly struct AudioEventId : IEquatable<AudioEventId>
    {
        private readonly string _value;

        public AudioEventId(string value)
        {
            _value = value ?? string.Empty;
        }

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public bool Equals(AudioEventId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AudioEventId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static AudioEventId From(string value)
        {
            return new AudioEventId(value);
        }
    }
}
