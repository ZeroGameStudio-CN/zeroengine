using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ZeroEngine.PlayerSettings
{
    public readonly struct SettingId : IEquatable<SettingId>
    {
        public SettingId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Setting ID cannot be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }
        public bool Equals(SettingId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SettingId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value;
        public static bool operator ==(SettingId left, SettingId right) => left.Equals(right);
        public static bool operator !=(SettingId left, SettingId right) => !left.Equals(right);
    }

    public enum SettingValueKind
    {
        Bool,
        Int,
        Float,
        String
    }

    public readonly struct SettingValue : IEquatable<SettingValue>
    {
        private readonly object _value;

        private SettingValue(SettingValueKind kind, object value)
        {
            Kind = kind;
            _value = value;
        }

        public SettingValueKind Kind { get; }
        public static SettingValue Bool(bool value) => new(SettingValueKind.Bool, value);
        public static SettingValue Int(int value) => new(SettingValueKind.Int, value);
        public static SettingValue Float(float value) => new(SettingValueKind.Float, value);
        public static SettingValue String(string value) => new(SettingValueKind.String, value ?? string.Empty);
        public bool AsBool() => Kind == SettingValueKind.Bool ? (bool)_value : throw WrongKind();
        public int AsInt() => Kind == SettingValueKind.Int ? (int)_value : throw WrongKind();
        public float AsFloat() => Kind == SettingValueKind.Float ? (float)_value : throw WrongKind();
        public string AsString() => Kind == SettingValueKind.String ? (string)_value : throw WrongKind();

        public string ToCanonicalString()
        {
            return Kind switch
            {
                SettingValueKind.Bool => AsBool() ? "true" : "false",
                SettingValueKind.Int => AsInt().ToString(CultureInfo.InvariantCulture),
                SettingValueKind.Float => AsFloat().ToString("R", CultureInfo.InvariantCulture),
                SettingValueKind.String => AsString(),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        public static bool TryParse(SettingValueKind kind, string text, out SettingValue value)
        {
            switch (kind)
            {
                case SettingValueKind.Bool when bool.TryParse(text, out var boolValue):
                    value = Bool(boolValue);
                    return true;
                case SettingValueKind.Int when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue):
                    value = Int(intValue);
                    return true;
                case SettingValueKind.Float when float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)
                                                  && !float.IsNaN(floatValue) && !float.IsInfinity(floatValue):
                    value = Float(floatValue);
                    return true;
                case SettingValueKind.String:
                    value = String(text);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public bool Equals(SettingValue other) => Kind == other.Kind && Equals(_value, other._value);
        public override bool Equals(object obj) => obj is SettingValue other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Kind, _value);
        public override string ToString() => ToCanonicalString();
        private InvalidOperationException WrongKind() => new($"Setting value is {Kind}.");
    }

    public enum SettingApplyPolicy
    {
        Preview,
        OnCommit,
        RestartRequired
    }

    public sealed class SettingDefinition
    {
        public SettingDefinition(
            SettingId id,
            string categoryId,
            SettingValue defaultValue,
            SettingApplyPolicy applyPolicy,
            string labelKey,
            string descriptionKey = "",
            int sortOrder = 0,
            bool visible = true,
            Func<SettingValue, bool> validator = null,
            Func<IReadOnlyList<string>> optionProvider = null,
            Func<bool> availability = null)
        {
            Id = id;
            CategoryId = categoryId ?? throw new ArgumentNullException(nameof(categoryId));
            DefaultValue = defaultValue;
            ApplyPolicy = applyPolicy;
            LabelKey = labelKey ?? throw new ArgumentNullException(nameof(labelKey));
            DescriptionKey = descriptionKey ?? string.Empty;
            SortOrder = sortOrder;
            Visible = visible;
            Validator = validator;
            OptionProvider = optionProvider;
            Availability = availability;
        }

        public SettingId Id { get; }
        public string CategoryId { get; }
        public SettingValueKind ValueKind => DefaultValue.Kind;
        public SettingValue DefaultValue { get; }
        public SettingApplyPolicy ApplyPolicy { get; }
        public string LabelKey { get; }
        public string DescriptionKey { get; }
        public int SortOrder { get; }
        public bool Visible { get; }
        public Func<SettingValue, bool> Validator { get; }
        public Func<IReadOnlyList<string>> OptionProvider { get; }
        public Func<bool> Availability { get; }
        public bool IsAvailable => Availability?.Invoke() ?? true;

        public bool IsValid(SettingValue value)
        {
            if (value.Kind != ValueKind || (value.Kind == SettingValueKind.Float &&
                                            (float.IsNaN(value.AsFloat()) || float.IsInfinity(value.AsFloat()))))
            {
                return false;
            }

            if (OptionProvider != null && value.Kind == SettingValueKind.String)
            {
                var options = OptionProvider();
                var found = false;
                for (var i = 0; i < options.Count; i++)
                {
                    found |= string.Equals(options[i], value.AsString(), StringComparison.Ordinal);
                }

                if (!found)
                {
                    return false;
                }
            }

            return Validator?.Invoke(value) ?? true;
        }
    }

    public sealed class SettingsCatalog
    {
        private readonly Dictionary<SettingId, SettingDefinition> _definitions;

        public SettingsCatalog(IEnumerable<SettingDefinition> definitions)
        {
            _definitions = new Dictionary<SettingId, SettingDefinition>();
            foreach (var definition in definitions ?? throw new ArgumentNullException(nameof(definitions)))
            {
                if (definition == null || !_definitions.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException($"Duplicate or null setting definition: {definition?.Id}");
                }

                if (!definition.IsValid(definition.DefaultValue))
                {
                    throw new ArgumentException($"Invalid default value: {definition.Id}");
                }
            }
        }

        public IReadOnlyCollection<SettingDefinition> Definitions => _definitions.Values;
        public bool TryGet(SettingId id, out SettingDefinition definition) => _definitions.TryGetValue(id, out definition);
    }

    public sealed class SettingsSnapshot
    {
        private readonly IReadOnlyDictionary<SettingId, SettingValue> _values;

        public SettingsSnapshot(IDictionary<SettingId, SettingValue> values)
        {
            _values = new ReadOnlyDictionary<SettingId, SettingValue>(
                new Dictionary<SettingId, SettingValue>(values));
        }

        public IReadOnlyDictionary<SettingId, SettingValue> Values => _values;
        public SettingValue this[SettingId id] => _values[id];
        public bool TryGet(SettingId id, out SettingValue value) => _values.TryGetValue(id, out value);
        internal Dictionary<SettingId, SettingValue> CopyValues() => new(_values);
    }
}
