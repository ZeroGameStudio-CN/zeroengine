using System;
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

        public string Value => _value ?? string.Empty;
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

        public static bool operator ==(StatId left, StatId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StatId left, StatId right)
        {
            return !left.Equals(right);
        }

        public static implicit operator StatId(string value)
        {
            return new StatId(value);
        }

        public static implicit operator string(StatId id)
        {
            return id.Value;
        }

        public static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }

    public enum StatValueKind
    {
        Integer,
        Float,
        Percent,
        Multiplier
    }

    [Serializable]
    public sealed class StatDefinition
    {
        public StatId Id;
        public string DisplayName;
        [TextArea(2, 4)] public string Description;
        public string Group;
        public int SortOrder;
        public StatValueKind ValueKind;
        public float DefaultValue;
        public float MinValue = float.MinValue;
        public float MaxValue = float.MaxValue;
        public string ExcelColumn;
        public bool ShowInCharacterEditor = true;
    }

    public interface IStatProvider
    {
        float GetStatValue(StatId id);
    }
}
