using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    // Layout snapshots deliberately retain transferred entries so taking loot never repacks the rest.
    public static class ExtractionContainerLayoutService
    {
        public static bool TryEnsure(ExtractionRaidContainerManifest container,
            ExtractionPlayableConfig config, out bool changed)
        {
            changed = false;
            if (container == null || config == null || !container.Opened) return false;
            if (container.LayoutVersion == 1) return true;
            if (container.LayoutVersion != 0) return false;
            int rows = container.Rows, columns = container.Columns;
            if (rows <= 0 || columns <= 0)
            {
                var definition = config.ContainerDefinitions.Find(d => d != null
                    && d.ContainerTypeId == container.ContainerTypeId);
                rows = definition?.Rows ?? 0;
                columns = definition?.Columns ?? 0;
                // Legacy/mod configurations defined a linear capacity only.
                if (rows <= 0 || columns <= 0) { rows = 1; columns = Math.Max(1, container.Capacity); }
            }
            var grid = new ExtractionItemGrid(columns, rows);
            var layouts = new List<ExtractionItemPlacement>();
            var overflow = new List<bool>();
            int overflowY = 0;
            foreach (var entry in container.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EntryId)
                    || !config.TryGetItemDefinition(entry.DefinitionId, out var item)
                    || item.Width <= 0 || item.Height <= 0) return false;
                bool fits = grid.TryFindFreeSlotWithRotation(item.Width, item.Height,
                    item.CanRotate, out int x, out int y, out bool rotated);
                int width = rotated ? item.Height : item.Width;
                int height = rotated ? item.Width : item.Height;
                if (!fits) { x = 0; y = overflowY; rotated = false; width = item.Width; height = item.Height; overflowY += height; }
                var placement = new ExtractionItemPlacement(entry.EntryId, x, y, width, height, rotated);
                if (fits) grid.Placements.Add(placement);
                layouts.Add(placement);
                overflow.Add(!fits);
            }
            for (int i = 0; i < container.Entries.Count; i++)
            {
                container.Entries[i].Layout = layouts[i];
                container.Entries[i].LayoutOverflow = overflow[i];
            }
            container.Rows = rows;
            container.Columns = columns;
            container.LayoutVersion = 1;
            changed = true;
            return true;
        }
    }
}
