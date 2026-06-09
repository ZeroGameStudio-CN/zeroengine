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
    }

    public static class TceComponentPalette
    {
        public static IReadOnlyList<TceComponentPaletteItem> BuildItems()
        {
            return TceComponentCatalogBuilder.Build()
                .Select(entry => new TceComponentPaletteItem(entry))
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
