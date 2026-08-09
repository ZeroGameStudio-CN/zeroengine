using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZeroGameStudio.ConfigPipeline
{
    public enum ConfigNodeKind
    {
        Null,
        Boolean,
        Integer,
        Number,
        String,
        Array,
        Object
    }

    public enum ConfigNumberType
    {
        Float32,
        Float64
    }

    public abstract class ConfigNode
    {
        protected ConfigNode(ConfigNodeKind kind)
        {
            Kind = kind;
        }

        public ConfigNodeKind Kind { get; }
    }

    public sealed class ConfigNullNode : ConfigNode
    {
        private ConfigNullNode()
            : base(ConfigNodeKind.Null)
        {
        }

        public static ConfigNullNode Instance { get; } = new ConfigNullNode();
    }

    public sealed class ConfigBooleanNode : ConfigNode
    {
        public ConfigBooleanNode(bool value)
            : base(ConfigNodeKind.Boolean)
        {
            Value = value;
        }

        public bool Value { get; }
    }

    public sealed class ConfigIntegerNode : ConfigNode
    {
        public ConfigIntegerNode(long value)
            : base(ConfigNodeKind.Integer)
        {
            Value = value;
        }

        public long Value { get; }
    }

    public sealed class ConfigNumberNode : ConfigNode
    {
        public ConfigNumberNode(float value)
            : base(ConfigNodeKind.Number)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Config numbers must be finite.");
            }

            NumberType = ConfigNumberType.Float32;
            Value = value;
        }

        public ConfigNumberNode(double value)
            : base(ConfigNodeKind.Number)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Config numbers must be finite.");
            }

            NumberType = ConfigNumberType.Float64;
            Value = value;
        }

        public ConfigNumberType NumberType { get; }

        public double Value { get; }

        public float Float32Value => (float)Value;
    }

    public sealed class ConfigStringNode : ConfigNode
    {
        public ConfigStringNode(string value)
            : base(ConfigNodeKind.String)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Value { get; }
    }

    public sealed class ConfigArrayNode : ConfigNode
    {
        private readonly ReadOnlyCollection<ConfigNode> items;

        public ConfigArrayNode(IEnumerable<ConfigNode> items)
            : base(ConfigNodeKind.Array)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var copiedItems = new List<ConfigNode>();
            foreach (ConfigNode item in items)
            {
                copiedItems.Add(item ?? throw new ArgumentException("Array items cannot be null.", nameof(items)));
            }

            this.items = copiedItems.AsReadOnly();
        }

        public IReadOnlyList<ConfigNode> Items => items;
    }

    public sealed class ConfigProperty
    {
        public ConfigProperty(string name, ConfigNode value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Property names cannot be empty.", nameof(name));
            }

            Name = name;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Name { get; }

        public ConfigNode Value { get; }
    }

    public sealed class ConfigObjectNode : ConfigNode
    {
        private readonly ReadOnlyCollection<ConfigProperty> properties;
        private readonly ReadOnlyDictionary<string, ConfigNode> valuesByName;

        public ConfigObjectNode(IEnumerable<ConfigProperty> properties)
            : base(ConfigNodeKind.Object)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            var copiedProperties = new List<ConfigProperty>();
            var lookup = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
            foreach (ConfigProperty property in properties)
            {
                if (property == null)
                {
                    throw new ArgumentException("Object properties cannot be null.", nameof(properties));
                }

                if (!lookup.TryAdd(property.Name, property.Value))
                {
                    throw new ArgumentException(
                        "Duplicate object property '" + property.Name + "'.",
                        nameof(properties));
                }

                copiedProperties.Add(property);
            }

            this.properties = copiedProperties.AsReadOnly();
            valuesByName = new ReadOnlyDictionary<string, ConfigNode>(lookup);
        }

        public IReadOnlyList<ConfigProperty> Properties => properties;

        public bool TryGetValue(string propertyName, out ConfigNode value)
        {
            return valuesByName.TryGetValue(propertyName, out value);
        }
    }
}
