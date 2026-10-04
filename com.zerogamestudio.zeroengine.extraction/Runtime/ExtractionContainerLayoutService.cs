using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    // Layout snapshots deliberately retain transferred entries so taking loot never repacks the rest.
    public static class ExtractionContainerLayoutService
    {
        // Read-only reservation for generation; existing committed loot is never removed to make room.
        internal static bool TryBuildGenerationGrid(ExtractionRaidContainerManifest container,
            ExtractionPlayableConfig config, out ExtractionItemGrid grid, out bool hasOverflow)
        {
            grid = null;
            hasOverflow = false;
            if (container == null || config == null || container.Entries == null) return false;
            ResolveDimensions(container, config, out int columns, out int rows);
            var reserved = new ExtractionItemGrid(columns, rows);
            foreach (var entry in container.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EntryId)
                    || !config.TryGetItemDefinition(entry.DefinitionId, out var item)
                    || item.Width <= 0 || item.Height <= 0) return false;
                if (!TryReserve(reserved, entry.EntryId, item)) hasOverflow = true;
            }
            grid = reserved;
            return true;
        }

        internal static bool CanFit(ExtractionItemGrid grid, ExtractionItemDefinition item)
            => grid != null && item != null && grid.TryFindFreeSlotWithRotation(item.Width, item.Height,
                item.CanRotate, out _, out _, out _);

        internal static bool TryReserve(ExtractionItemGrid grid, string entryId, ExtractionItemDefinition item)
        {
            if (!grid.TryFindFreeSlotWithRotation(item.Width, item.Height, item.CanRotate,
                    out int x, out int y, out bool rotated)) return false;
            grid.Placements.Add(new ExtractionItemPlacement(entryId, x, y,
                rotated ? item.Height : item.Width, rotated ? item.Width : item.Height, rotated));
            return true;
        }

        private static void ResolveDimensions(ExtractionRaidContainerManifest container,
            ExtractionPlayableConfig config, out int columns, out int rows)
        {
            rows = container.Rows;
            columns = container.Columns;
            if (rows > 0 && columns > 0) return;
            var definition = config.ContainerDefinitions.Find(d => d != null
                && d.ContainerTypeId == container.ContainerTypeId);
            rows = definition?.Rows ?? 0;
            columns = definition?.Columns ?? 0;
            if (rows <= 0 || columns <= 0) { rows = 1; columns = Math.Max(1, container.Capacity); }
        }

        public static bool TryEnsure(ExtractionRaidContainerManifest container,
            ExtractionPlayableConfig config, out bool changed)
        {
            changed = false;
            if (container == null || config == null || !container.Opened) return false;
            if (container.LayoutVersion == 1) return true;
            if (container.LayoutVersion != 0) return false;
            ResolveDimensions(container, config, out int columns, out int rows);
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
