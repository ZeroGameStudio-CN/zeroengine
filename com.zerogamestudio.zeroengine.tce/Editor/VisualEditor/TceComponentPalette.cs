using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.TCE.Editor
{
    public readonly struct TceComponentPaletteItem
    {
        public TceComponentPaletteItem(TceComponentCatalogEntry entry)
        {
            Entry = entry;
        }

        public TceComponentCatalogEntry Entry { get; }
        public Type DataType => Entry.DataType;
        public TceComponentDocCategory Category => Entry.Category;
        public string DisplayName => Entry.DisplayName;
        public string Description => Entry.ShortDescription;
        public TceGraphLane Lane => Category switch
        {
            TceComponentDocCategory.Trigger => TceGraphLane.Trigger,
            TceComponentDocCategory.Condition => TceGraphLane.Condition,
            TceComponentDocCategory.Effect => TceGraphLane.Effect,
            _ => TceGraphLane.Effect
        };
        public string Label => $"{DisplayName} ({Lane})";
    }

    public readonly struct TceComponentPaletteGroup
    {
        public TceComponentPaletteGroup(TceGraphLane lane, IReadOnlyList<TceComponentPaletteItem> items)
        {
            Lane = lane;
            Items = items ?? Array.Empty<TceComponentPaletteItem>();
        }

        public TceGraphLane Lane { get; }
        public IReadOnlyList<TceComponentPaletteItem> Items { get; }
    }

    public static class TceComponentPalette
    {
        public static IReadOnlyList<TceComponentPaletteItem> BuildItems()
        {
            return TceComponentCatalogBuilder.Build()
                .Select(entry => new TceComponentPaletteItem(entry))
                .OrderBy(item => item.Lane)
                .ThenBy(item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Entry.DataTypeFullName, StringComparer.Ordinal)
                .ToArray();
        }

        public static IReadOnlyList<TceComponentPaletteGroup> BuildGroups()
        {
            return BuildItems()
                .GroupBy(item => item.Lane)
                .OrderBy(group => group.Key)
                .Select(group => new TceComponentPaletteGroup(group.Key, group.ToArray()))
                .ToArray();
        }

        public static IReadOnlyList<TceComponentPaletteItem> Search(string query)
        {
            string text = query ?? string.Empty;
            return BuildItems()
                .Where(item =>
                    item.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Entry.DataTypeFullName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Entry.RuntimeTypeFullName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
        }

        public static TceComponentData CreateData(TceComponentPaletteItem item)
        {
            return (TceComponentData)Activator.CreateInstance(item.DataType);
        }
    }
}
