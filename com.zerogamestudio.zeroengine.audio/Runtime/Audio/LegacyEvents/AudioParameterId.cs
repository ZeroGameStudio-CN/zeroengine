using System;

namespace ZeroEngine.Audio.Events
{
    [Serializable]
    public readonly struct AudioParameterId : IEquatable<AudioParameterId>
    {
        private readonly string _value;

        public AudioParameterId(string value)
        {
            _value = value ?? string.Empty;
        }

        public string Value => _value ?? string.Empty;
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public bool Equals(AudioParameterId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AudioParameterId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static AudioParameterId From(string value)
        {
            return new AudioParameterId(value);
        }
    }
}
