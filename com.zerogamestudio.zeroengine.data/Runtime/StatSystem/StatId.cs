using System;
using System.Text;
using UnityEngine;

namespace ZeroEngine.StatSystem
{
    [Serializable]
    public struct StatId : IEquatable<StatId>
    {
        [SerializeField] private string _value;

        public StatId(string value)
        {
            _value = Normalize(value);
        }

        public string Value => Normalize(_value);
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        public bool Equals(StatId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is StatId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public StatType ToStatTypeOrDefault(StatType fallback = StatType.None)
        {
            return TryParseStatType(this, out var statType) ? statType : fallback;
        }

        public static bool TryParseStatType(StatId statId, out StatType statType)
        {
            var value = statId.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                statType = StatType.None;
                return false;
            }

            var lastSegmentStart = value.LastIndexOf('.');
            var localName = lastSegmentStart >= 0 && lastSegmentStart < value.Length - 1
                ? value.Substring(lastSegmentStart + 1)
                : value;
            var normalizedLocalName = NormalizeForStatTypeMatch(localName);
            foreach (StatType candidate in Enum.GetValues(typeof(StatType)))
            {
                if (candidate == StatType.None)
                {
                    continue;
                }

                if (string.Equals(
                        NormalizeForStatTypeMatch(candidate.ToString()),
                        normalizedLocalName,
                        StringComparison.Ordinal))
                {
                    statType = candidate;
                    return true;
                }
            }

            statType = StatType.None;
            return false;
        }

        public static implicit operator StatId(string value)
        {
            return new StatId(value);
        }

        public static implicit operator string(StatId statId)
        {
            return statId.Value;
        }

        public static bool operator ==(StatId left, StatId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StatId left, StatId right)
        {
            return !left.Equals(right);
        }

        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            foreach (var character in trimmed)
            {
                if (character > '\u007f' || character is >= 'A' and <= 'Z')
                {
                    return trimmed.ToLowerInvariant();
                }
            }

            return trimmed;
        }

        private static string NormalizeForStatTypeMatch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }
    }
}
