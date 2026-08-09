using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ZeroEngine.TCE
{
    public sealed class TceComponentRegistryEntry
    {
        public TceComponentRegistryEntry(string componentId, Type dataType, TceComponentDocCategory category)
        {
            ComponentId = componentId ?? string.Empty;
            DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
            Category = category;
        }

        public string ComponentId { get; }
        public Type DataType { get; }
        public TceComponentDocCategory Category { get; }
    }

    public sealed class TceComponentRegistry
    {
        private readonly Dictionary<string, TceComponentRegistryEntry> entriesById;

        private TceComponentRegistry(IEnumerable<TceComponentRegistryEntry> entries)
        {
            entriesById = new Dictionary<string, TceComponentRegistryEntry>(StringComparer.Ordinal);

            foreach (TceComponentRegistryEntry entry in entries)
            {
                if (entriesById.ContainsKey(entry.ComponentId))
                    throw new ArgumentException($"Duplicate TCE component ID '{entry.ComponentId}'.", nameof(entries));

                entriesById.Add(entry.ComponentId, entry);
            }
        }

        public static TceComponentRegistry CreateDefault()
        {
            Type[] dataTypes = typeof(TceComponentData).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && typeof(TceComponentData).IsAssignableFrom(type))
                .ToArray();

            return Create(dataTypes);
        }

        public static TceComponentRegistry Create(params Type[] allowedDataTypes)
        {
            return Create((IEnumerable<Type>)allowedDataTypes);
        }

        public static TceComponentRegistry Create(IEnumerable<Type> allowedDataTypes)
        {
            IEnumerable<TceComponentRegistryEntry> entries = (allowedDataTypes ?? Array.Empty<Type>())
                .Select(BuildEntry)
                .Where(entry => entry != null);

            return new TceComponentRegistry(entries);
        }

        public bool TryGet(string componentId, out TceComponentRegistryEntry entry)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                entry = null;
                return false;
            }

            return entriesById.TryGetValue(componentId, out entry);
        }

        private static TceComponentRegistryEntry BuildEntry(Type dataType)
        {
            if (dataType == null || dataType.IsAbstract || !typeof(TceComponentData).IsAssignableFrom(dataType))
                return null;

            TceComponentDocAttribute doc = dataType.GetCustomAttribute<TceComponentDocAttribute>();
            if (doc == null || string.IsNullOrEmpty(doc.ComponentId))
                return null;

            return new TceComponentRegistryEntry(doc.ComponentId, dataType, doc.Category);
        }
    }
}
