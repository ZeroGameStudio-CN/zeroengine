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

        private static Type[] cachedTypes;

        public static IReadOnlyList<Type> GetManageableScriptableObjectTypes()
        {
            if (cachedTypes != null)
            {
                return cachedTypes;
            }

            cachedTypes = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .Where(type => type != null && !type.IsAbstract)
                .Where(HasManageableDataAttribute)
                .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return cachedTypes;
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
    }
}
