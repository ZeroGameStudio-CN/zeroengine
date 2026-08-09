using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZeroGameStudio.ConfigPipeline
{
    public enum ConfigSchemaType
    {
        Object,
        Array,
        String,
        Integer,
        Number,
        Boolean
    }

    public enum ConfigFieldScope
    {
        Shared,
        Client,
        Server
    }

    public enum ConfigIntegerType
    {
        Int32,
        Int64
    }

    public sealed class ConfigSchema
    {
        internal ConfigSchema(
            string schemaId,
            int schemaVersion,
            string schemaHash,
            ConfigObjectNode sourceNode,
            ConfigSchemaNode root)
        {
            SchemaId = schemaId;
            SchemaVersion = schemaVersion;
            SchemaHash = schemaHash;
            SourceNode = sourceNode;
            Root = root;
        }

        public string SchemaId { get; }

        public int SchemaVersion { get; }

        public string SchemaHash { get; }

        public ConfigObjectNode SourceNode { get; }

        public ConfigSchemaNode Root { get; }
    }

    public sealed class ConfigSchemaNode
    {
        private readonly ReadOnlyCollection<ConfigSchemaProperty> properties;
        private readonly ReadOnlyCollection<ConfigNode> enumValues;
        private readonly HashSet<string> requiredProperties;

        internal ConfigSchemaNode(
            ConfigSchemaType type,
            IEnumerable<ConfigSchemaProperty> properties,
            IEnumerable<string> requiredProperties,
            ConfigSchemaNode items,
            ConfigNode defaultValue,
            IEnumerable<ConfigNode> enumValues,
            ConfigIntegerType? integerType,
            ConfigNumberType? numberType,
            double? minimum,
            double? maximum,
            double? exclusiveMinimum,
            double? exclusiveMaximum,
            string pattern,
            int? minItems,
            int? maxItems,
            bool uniqueItems,
            ConfigFieldScope scope,
            bool authoringOnly,
            bool primaryKey,
            string referencePath,
            string sheet,
            string parentKey,
            string orderField,
            string assetType,
            bool localizationKey,
            string title,
            string description,
            string unit,
            string group)
        {
            Type = type;
            this.properties = new List<ConfigSchemaProperty>(
                properties ?? Array.Empty<ConfigSchemaProperty>()).AsReadOnly();
            this.requiredProperties = new HashSet<string>(
                requiredProperties ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            Items = items;
            DefaultValue = defaultValue;
            this.enumValues = new List<ConfigNode>(
                enumValues ?? Array.Empty<ConfigNode>()).AsReadOnly();
            IntegerType = integerType;
            NumberType = numberType;
            Minimum = minimum;
            Maximum = maximum;
            ExclusiveMinimum = exclusiveMinimum;
            ExclusiveMaximum = exclusiveMaximum;
            Pattern = pattern;
            MinItems = minItems;
            MaxItems = maxItems;
            UniqueItems = uniqueItems;
            Scope = scope;
            AuthoringOnly = authoringOnly;
            PrimaryKey = primaryKey;
            ReferencePath = referencePath;
            Sheet = sheet;
            ParentKey = parentKey;
            OrderField = orderField;
            AssetType = assetType;
            LocalizationKey = localizationKey;
            Title = title;
            Description = description;
            Unit = unit;
            Group = group;
        }

        public ConfigSchemaType Type { get; }

        public IReadOnlyList<ConfigSchemaProperty> Properties => properties;

        public ConfigSchemaNode Items { get; }

        public ConfigNode DefaultValue { get; }

        public IReadOnlyList<ConfigNode> EnumValues => enumValues;

        public ConfigIntegerType? IntegerType { get; }

        public ConfigNumberType? NumberType { get; }

        public double? Minimum { get; }

        public double? Maximum { get; }

        public double? ExclusiveMinimum { get; }

        public double? ExclusiveMaximum { get; }

        public string Pattern { get; }

        public int? MinItems { get; }

        public int? MaxItems { get; }

        public bool UniqueItems { get; }

        public ConfigFieldScope Scope { get; }

        public bool AuthoringOnly { get; }

        public bool PrimaryKey { get; }

        public string ReferencePath { get; }

        public string Sheet { get; }

        public string ParentKey { get; }

        public string OrderField { get; }

        public string AssetType { get; }

        public bool LocalizationKey { get; }

        public string Title { get; }

        public string Description { get; }

        public string Unit { get; }

        public string Group { get; }

        public bool IsRequired(string propertyName)
        {
            return requiredProperties.Contains(propertyName);
        }
    }

    public sealed class ConfigSchemaProperty
    {
        internal ConfigSchemaProperty(string name, ConfigSchemaNode schema)
        {
            Name = name;
            Schema = schema;
        }

        public string Name { get; }

        public ConfigSchemaNode Schema { get; }
    }
}
