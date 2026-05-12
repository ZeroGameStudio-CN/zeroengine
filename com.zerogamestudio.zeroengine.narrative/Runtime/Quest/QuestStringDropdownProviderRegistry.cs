using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// Editor-facing registry for string dropdown data sources.
    /// Stored values remain strings; missing providers simply produce an empty option list.
    /// </summary>
    public static class QuestStringDropdownProviderRegistry
    {
        private static readonly Dictionary<QuestStringDropdownKind, Func<IEnumerable<string>>> Providers = new();
        private static readonly Dictionary<QuestStringDropdownKind, List<string>> CachedOptions = new();
        private static readonly HashSet<QuestStringDropdownKind> DirtyKinds = new();

        static QuestStringDropdownProviderRegistry()
        {
            Register(QuestStringDropdownKind.EventName, GetQuestEventNames);
        }

        public static void Register(QuestStringDropdownKind kind, Func<IEnumerable<string>> provider)
        {
            if (provider == null)
            {
                Providers.Remove(kind);
            }
            else
            {
                Providers[kind] = provider;
            }

            Refresh(kind);
        }

        public static void Refresh(QuestStringDropdownKind kind)
        {
            DirtyKinds.Add(kind);
        }

        public static void Refresh()
        {
            foreach (QuestStringDropdownKind kind in Enum.GetValues(typeof(QuestStringDropdownKind)))
                DirtyKinds.Add(kind);
        }

        public static IReadOnlyList<string> GetOptions(QuestStringDropdownKind kind)
        {
            if (!CachedOptions.TryGetValue(kind, out var options) || DirtyKinds.Contains(kind))
            {
                options = BuildOptions(kind);
                CachedOptions[kind] = options;
                DirtyKinds.Remove(kind);
            }

            return options;
        }

        private static List<string> BuildOptions(QuestStringDropdownKind kind)
        {
            if (!Providers.TryGetValue(kind, out var provider))
                return new List<string>();

            return provider()
                ?.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList() ?? new List<string>();
        }

        private static IEnumerable<string> GetQuestEventNames()
        {
            return new[]
            {
                QuestEvents.EntityKilled,
                QuestEvents.ItemObtained,
                QuestEvents.Interacted,
                QuestEvents.LocationReached
            };
        }
    }
}
