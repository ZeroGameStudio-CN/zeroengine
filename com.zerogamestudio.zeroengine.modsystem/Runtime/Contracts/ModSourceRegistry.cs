using System;
using System.Collections.Generic;

namespace ZeroEngine.ModSystem
{
    public static class ModSourceRegistry
    {
        private static readonly List<IModSource> Sources = new();

        public static IReadOnlyList<IModSource> RegisteredSources => Sources;

        public static bool Register(IModSource source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.SourceId))
                return false;

            foreach (var registered in Sources)
            {
                if (string.Equals(registered.SourceId, source.SourceId, StringComparison.Ordinal))
                    return false;
            }

            Sources.Add(source);
            return true;
        }

        public static void Clear()
        {
            Sources.Clear();
        }
    }
}
