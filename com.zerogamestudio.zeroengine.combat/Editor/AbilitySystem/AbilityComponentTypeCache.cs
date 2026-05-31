using System;
using System.Collections.Generic;
using System.Linq;

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

            var result = AbilityComponentDocUtility.GetConcreteComponentDefinitionTypes()
                .Where(type => baseType.IsAssignableFrom(type))
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
    }
}
