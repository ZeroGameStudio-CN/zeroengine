namespace POB.Extraction
{
    public static class ExtractionHostileExplorerLootService
    {
        public static bool TryRollExplorerLoot(
            ExtractionHostileExplorerDefinition encounter,
            IExtractionLootTableCatalog lootTables,
            IExtractionItemCatalog itemCatalog,
            string instanceId,
            int rollValue,
            out ExtractionLootPickup pickup)
        {
            pickup = null;
            if (encounter == null || !encounter.IsValid) return false;
            if (lootTables == null || itemCatalog == null) return false;
            ExtractionLootRollTable table;
            if (!string.IsNullOrEmpty(encounter.ContainerTypeId))
            {
                // This existing API grants one pickup per call. Container identity resolves its
                // shared loot pool; it must never reinterpret an invalid container as a table ID.
                if (!(lootTables is ExtractionPlayableConfig config)
                    || !ExtractionRaidLootManifestGenerator.TryGetContainer(config, encounter.ContainerTypeId, out var container)
                    || container.LootTableIds == null || container.LootTableIds.Count == 0) return false;
                table = new ExtractionLootRollTable();
                foreach (string tableId in container.LootTableIds)
                {
                    if (!lootTables.TryGetLootTable(tableId, out var child) || !HasKnownDefinitions(child, itemCatalog)) return false;
                    table.Entries.AddRange(child.Entries);
                }
            }
            else
            {
                if (!lootTables.TryGetLootTable(encounter.LootTableId, out var legacyTable)) return false;
                table = legacyTable;
            }
            if (!HasKnownDefinitions(table, itemCatalog)) return false;

            return ExtractionLootRollService.TryRollPickup(
                table,
                itemCatalog,
                instanceId,
                rollValue,
                out pickup);
        }

        private static bool HasKnownDefinitions(
            ExtractionLootRollTable table,
            IExtractionItemCatalog itemCatalog)
        {
            if (table == null || table.Entries == null) return false;

            foreach (var entry in table.Entries)
            {
                if (entry == null || !entry.IsValid) continue;
                if (!itemCatalog.TryGetItemDefinition(entry.DefinitionId, out var definition)) return false;
                if (definition == null) return false;
            }

            return true;
        }
    }
}
