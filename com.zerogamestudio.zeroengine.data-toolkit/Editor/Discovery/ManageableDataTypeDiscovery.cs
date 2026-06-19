using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZGS.DataToolkit.Editor
{
    public static class ManageableDataTypeDiscovery
    {
        private const string LegacyAttributeName = "ManageableDataAttribute";
        private const string ToolkitAttributeFullName = "ZGS.DataToolkit.ManageableDataAttribute";
        private static readonly string[] ConfigTypeSuffixes =
        {
            "Config",
            "ConfigSO",
            "Data",
            "DataSO",
            "Database",
            "DatabaseSO",
            "Definition",
            "DefinitionSO",
            "Preset",
            "PresetSO",
            "RecipeSO",
            "TableSO",
            "TreeAsset"
        };

        private static Type[] cachedTypes;

        public static IReadOnlyList<Type> GetManageableScriptableObjectTypes()
        {
            if (cachedTypes != null)
            {
                return cachedTypes;
            }

            cachedTypes = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(type => type != null && !type.IsAbstract)
                .Where(IsManageableScriptableObjectType)
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return cachedTypes;
        }

        public static bool IsManageableScriptableObjectType(Type type)
        {
            return type != null &&
                   !type.IsAbstract &&
                   typeof(ScriptableObject).IsAssignableFrom(type) &&
                   (HasManageableDataAttribute(type) ||
                    HasCreateAssetMenuAttribute(type) ||
                    IsZeroEngineConfigLikeType(type));
        }

        public static void ClearCache()
        {
            cachedTypes = null;
        }

        private static bool HasManageableDataAttribute(Type type)
        {
            foreach (var attribute in type.GetCustomAttributes(inherit: false))
            {
                var attributeType = attribute.GetType();
                if (attributeType.Name == LegacyAttributeName ||
                    attributeType.FullName == ToolkitAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCreateAssetMenuAttribute(Type type)
        {
            return type.GetCustomAttributes(typeof(CreateAssetMenuAttribute), inherit: false).Length > 0;
        }

        private static bool IsZeroEngineConfigLikeType(Type type)
        {
            if (!IsZeroEngineNamespace(type.Namespace))
            {
                return false;
            }

            return ConfigTypeSuffixes.Any(suffix =>
                type.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsZeroEngineNamespace(string typeNamespace)
        {
            if (string.IsNullOrWhiteSpace(typeNamespace))
            {
                return false;
            }

            return typeNamespace.StartsWith("ZeroEngine", StringComparison.Ordinal) ||
                   typeNamespace.StartsWith("ZGS", StringComparison.Ordinal);
        }
    }
}
