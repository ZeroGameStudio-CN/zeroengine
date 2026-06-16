using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ZeroEngine.AbilitySystem.Editor
{
    public static class AbilityComponentTypeCache
    {
        private static readonly Dictionary<Type, Type[]> Cache = new();

        public static IReadOnlyList<Type> GetComponentTypes(Type baseType)
        {
            if (baseType == null)
            {
                return Array.Empty<Type>();
            }

            if (Cache.TryGetValue(baseType, out var cached))
            {
                return cached;
            }

            var result = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(IsVisibleConcreteComponent)
                .Where(type => !Attribute.IsDefined(type, typeof(ObsoleteAttribute)))
                .OrderBy(type => AbilityComponentDocUtility.GetDoc(type).DisplayName, StringComparer.Ordinal)
                .ToArray();
            Cache[baseType] = result;
            return result;
        }

        public static IReadOnlyList<Type> GetComponentTypes<TBase>()
        {
            return GetComponentTypes(typeof(TBase));
        }

        private static bool IsVisibleConcreteComponent(Type type)
        {
            return type != null
                   && type.IsClass
                   && !type.IsAbstract
                   && !type.ContainsGenericParameters
                   && (type.IsPublic || type.IsNestedPublic);
        }
    }
}
