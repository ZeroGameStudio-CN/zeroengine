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
            if (!lootTables.TryGetLootTable(encounter.LootTableId, out var table)) return false;
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
