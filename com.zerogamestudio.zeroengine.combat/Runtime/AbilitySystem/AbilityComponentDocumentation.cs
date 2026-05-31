using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ZeroEngine.AbilitySystem
{
    public enum AbilityComponentDocCategory
    {
        Trigger,
        Condition,
        Effect
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AbilityComponentDocAttribute : Attribute
    {
        public AbilityComponentDocAttribute(
            AbilityComponentDocCategory category,
            string displayName,
            string shortDescription,
            string expandedDescription)
        {
            Category = category;
            DisplayName = displayName;
            ShortDescription = shortDescription;
            ExpandedDescription = expandedDescription;
        }

        public AbilityComponentDocCategory Category { get; }
        public string DisplayName { get; }
        public string ShortDescription { get; }
        public string ExpandedDescription { get; }
    }

    public readonly struct AbilityComponentDocInfo
    {
        public AbilityComponentDocInfo(
            Type componentType,
            AbilityComponentDocCategory category,
            string displayName,
            string shortDescription,
            string expandedDescription)
        {
            ComponentType = componentType;
            Category = category;
            DisplayName = displayName;
            ShortDescription = shortDescription;
            ExpandedDescription = expandedDescription;
        }

        public Type ComponentType { get; }
        public AbilityComponentDocCategory Category { get; }
        public string DisplayName { get; }
        public string ShortDescription { get; }
        public string ExpandedDescription { get; }

        public bool HasDocumentation =>
            !string.IsNullOrWhiteSpace(ShortDescription)
            || !string.IsNullOrWhiteSpace(ExpandedDescription);
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class AbilityFieldDocAttribute : Attribute
    {
        public AbilityFieldDocAttribute(string displayName, string tooltip)
        {
            DisplayName = displayName;
            Tooltip = tooltip;
        }

        public string DisplayName { get; }
        public string Tooltip { get; }
    }

    public readonly struct AbilityFieldDocInfo
    {
        public AbilityFieldDocInfo(FieldInfo field, string displayName, string tooltip)
        {
            Field = field;
            DisplayName = displayName;
            Tooltip = tooltip;
        }

        public FieldInfo Field { get; }
        public string DisplayName { get; }
        public string Tooltip { get; }
    }

    public static class AbilityComponentDocUtility
    {
        private static readonly Dictionary<Type, AbilityComponentDocInfo> Cache = new();

        public static AbilityComponentDocInfo GetDoc(Type componentType)
        {
            if (componentType == null)
            {
                return default;
            }

            if (Cache.TryGetValue(componentType, out var cached))
            {
                return cached;
            }

            var attribute = componentType.GetCustomAttribute<AbilityComponentDocAttribute>();
            var category = attribute?.Category ?? InferCategory(componentType);
            var displayName = FormatDisplayName(componentType, attribute?.DisplayName);
            var info = new AbilityComponentDocInfo(
                componentType,
                category,
                displayName,
                attribute?.ShortDescription ?? string.Empty,
                attribute?.ExpandedDescription ?? string.Empty);

            Cache[componentType] = info;
            return info;
        }

        public static IEnumerable<Type> GetConcreteComponentDefinitionTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .Where(IsConcreteAbilityComponentDefinition)
                .OrderBy(type => GetDoc(type).Category)
                .ThenBy(type => GetDoc(type).DisplayName, StringComparer.Ordinal);
        }

        public static IEnumerable<Type> GetConcreteComponentDefinitionTypes<TBase>()
        {
            var baseType = typeof(TBase);
            return GetConcreteComponentDefinitionTypes()
                .Where(type => baseType.IsAssignableFrom(type));
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static bool IsConcreteAbilityComponentDefinition(Type type)
        {
            return type != null
                   && type.IsClass
                   && !type.IsAbstract
                   && !type.ContainsGenericParameters
                   && (type.IsPublic || type.IsNestedPublic)
                   && (typeof(AbilityTriggerDefinition).IsAssignableFrom(type)
                       || typeof(AbilityConditionDefinition).IsAssignableFrom(type)
                       || typeof(AbilityEffectDefinition).IsAssignableFrom(type));
        }

        private static AbilityComponentDocCategory InferCategory(Type componentType)
        {
            if (typeof(AbilityTriggerDefinition).IsAssignableFrom(componentType))
            {
                return AbilityComponentDocCategory.Trigger;
            }

            if (typeof(AbilityConditionDefinition).IsAssignableFrom(componentType))
            {
                return AbilityComponentDocCategory.Condition;
            }

            return AbilityComponentDocCategory.Effect;
        }

        private static string FormatDisplayName(Type componentType, string displayName)
        {
            var fallback = SplitPascalName(componentType.Name);
            return string.IsNullOrWhiteSpace(displayName)
                ? fallback
                : $"{displayName} ({componentType.Name})";
        }

        private static string SplitPascalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(name.Length + 8);
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(name[i]);
            }

            return builder.ToString();
        }
    }

    public static class AbilityFieldDocUtility
    {
        private static readonly Dictionary<FieldInfo, AbilityFieldDocInfo> Cache = new();

        public static AbilityFieldDocInfo GetFieldDoc(FieldInfo field)
        {
            if (field == null)
            {
                return default;
            }

            if (Cache.TryGetValue(field, out var cached))
            {
                return cached;
            }

            var attribute = field.GetCustomAttribute<AbilityFieldDocAttribute>();
            var displayName = string.IsNullOrWhiteSpace(attribute?.DisplayName)
                ? SplitPascalName(field.Name)
                : attribute.DisplayName;
            var info = new AbilityFieldDocInfo(
                field,
                displayName,
                attribute?.Tooltip ?? string.Empty);

            Cache[field] = info;
            return info;
        }

        private static string SplitPascalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(name.Length + 8);
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(name[i]);
            }

            return builder.ToString();
        }
    }
}
