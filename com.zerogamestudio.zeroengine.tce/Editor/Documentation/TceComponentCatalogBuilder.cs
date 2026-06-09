using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ZeroEngine.TCE.Editor
{
    public sealed class TceComponentCatalogEntry
    {
        public TceComponentCatalogEntry(
            Type dataType,
            Type runtimeType,
            TceComponentDocCategory category,
            string displayName,
            string shortDescription,
            string expandedDescription,
            IReadOnlyList<TceComponentCatalogField> fields)
        {
            DataType = dataType;
            RuntimeType = runtimeType;
            Category = category;
            DisplayName = displayName ?? string.Empty;
            ShortDescription = shortDescription ?? string.Empty;
            ExpandedDescription = expandedDescription ?? string.Empty;
            Fields = fields ?? Array.Empty<TceComponentCatalogField>();
        }

        public Type DataType { get; }
        public Type RuntimeType { get; }
        public TceComponentDocCategory Category { get; }
        public string DisplayName { get; }
        public string ShortDescription { get; }
        public string ExpandedDescription { get; }
        public IReadOnlyList<TceComponentCatalogField> Fields { get; }
        public string DataTypeFullName => DataType.FullName;
        public string RuntimeTypeFullName => RuntimeType.FullName;
    }

    public sealed class TceComponentCatalogField
    {
        public TceComponentCatalogField(string name, string typeName, string defaultValue)
        {
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            DefaultValue = defaultValue ?? string.Empty;
        }

        public string Name { get; }
        public string TypeName { get; }
        public string DefaultValue { get; }
    }

    public static class TceComponentCatalogBuilder
    {
        public static IReadOnlyList<TceComponentCatalogEntry> Build()
        {
            return typeof(TceComponentData).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && typeof(TceComponentData).IsAssignableFrom(type))
                .Select(BuildEntry)
                .OrderBy(entry => entry.Category)
                .ThenBy(entry => entry.DisplayName, StringComparer.Ordinal)
                .ThenBy(entry => entry.DataTypeFullName, StringComparer.Ordinal)
                .ToArray();
        }

        private static TceComponentCatalogEntry BuildEntry(Type dataType)
        {
            var data = (TceComponentData)Activator.CreateInstance(dataType);
            TceComponentDocAttribute doc = dataType.GetCustomAttribute<TceComponentDocAttribute>();

            return new TceComponentCatalogEntry(
                dataType,
                data.RuntimeType,
                doc?.Category ?? InferCategory(dataType),
                doc?.DisplayName ?? dataType.Name,
                doc?.ShortDescription ?? string.Empty,
                doc?.ExpandedDescription ?? string.Empty,
                BuildFields(dataType, data));
        }

        private static TceComponentDocCategory InferCategory(Type dataType)
        {
            if (typeof(TceTriggerData).IsAssignableFrom(dataType))
                return TceComponentDocCategory.Trigger;

            if (typeof(TceEffectData).IsAssignableFrom(dataType))
                return TceComponentDocCategory.Effect;

            return TceComponentDocCategory.Condition;
        }

        private static IReadOnlyList<TceComponentCatalogField> BuildFields(Type dataType, object instance)
        {
            return dataType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.IsPublic || field.GetCustomAttribute<SerializeField>() != null)
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => new TceComponentCatalogField(
                    field.Name,
                    field.FieldType.FullName ?? field.FieldType.Name,
                    FormatDefaultValue(field.GetValue(instance))))
                .ToArray();
        }

        private static string FormatDefaultValue(object value)
        {
            if (value == null)
                return "null";

            if (value is string stringValue)
                return $"\"{stringValue.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

            if (value is bool boolValue)
                return boolValue ? "true" : "false";

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }
    }
}
