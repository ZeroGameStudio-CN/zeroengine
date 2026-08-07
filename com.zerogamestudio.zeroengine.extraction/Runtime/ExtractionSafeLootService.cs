using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionSafeLootService
    {
        public static bool TryGetSafeDefinitions(
            List<ExtractionSafeDefinition> safes,
            string mapId,
            List<ExtractionSafeDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (safes == null || string.IsNullOrEmpty(mapId)) return false;

            foreach (var safe in safes)
            {
                if (safe == null || !safe.IsValid) continue;
                if (safe.MapId != mapId) continue;
                results.Add(safe);
            }

            return results.Count > 0;
        }

        public static bool TryRollSafeLoot(
            ExtractionSafeDefinition safe,
            IExtractionLootTableCatalog lootTables,
            IExtractionItemCatalog itemCatalog,
            string keyItemDefinitionId,
            string instanceId,
            int rollValue,
            out ExtractionLootPickup pickup)
        {
            pickup = null;
            if (safe == null || !safe.IsValid) return false;
            if (lootTables == null || itemCatalog == null) return false;
            if (!safe.CanOpenWith(keyItemDefinitionId)) return false;
            if (!lootTables.TryGetLootTable(safe.LootTableId, out var table)) return false;

            return ExtractionLootRollService.TryRollPickup(
                table,
                itemCatalog,
                instanceId,
                rollValue,
                out pickup);
        }
    }
}
