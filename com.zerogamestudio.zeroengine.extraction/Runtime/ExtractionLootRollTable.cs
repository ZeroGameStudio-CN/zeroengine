using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionLootTableEntry
    {
        public string DefinitionId;
        public int Weight;
        public int Quantity;
        public bool CanEnterSecureContainer;

        public bool IsValid => !string.IsNullOrEmpty(DefinitionId) && Weight > 0 && Quantity > 0;

        public ExtractionLootTableEntry(
            string definitionId,
            int weight,
            int quantity,
            bool canEnterSecureContainer)
        {
            DefinitionId = definitionId;
            Weight = weight;
            Quantity = quantity;
            CanEnterSecureContainer = canEnterSecureContainer;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionLootRollTable
    {
        public List<ExtractionLootTableEntry> Entries = new();
    }

    public static class ExtractionLootRollService
    {
        public static bool TryRollPickup(
            ExtractionLootRollTable table,
            IExtractionItemCatalog catalog,
            string instanceId,
            int rollValue,
            out ExtractionLootPickup pickup)
        {
            pickup = null;
            if (table == null || table.Entries == null) return false;
            if (catalog == null || string.IsNullOrEmpty(instanceId)) return false;

            long totalWeight = GetTotalValidWeight(table, catalog);
            if (totalWeight <= 0) return false;

            long target = NormalizeRoll(rollValue, totalWeight);
            long cursor = 0;

            foreach (var entry in table.Entries)
            {
                if (!TryGetValidDefinition(entry, catalog, out var definition)) continue;

                cursor += entry.Weight;
                if (target >= cursor) continue;

                pickup = new ExtractionLootPickup(
                    new ExtractionItemInstance(instanceId, entry.DefinitionId, entry.Quantity),
                    definition,
                    entry.CanEnterSecureContainer);
                return pickup.IsValid;
            }

            return false;
        }

        private static long GetTotalValidWeight(
            ExtractionLootRollTable table,
            IExtractionItemCatalog catalog)
        {
            long totalWeight = 0;
            foreach (var entry in table.Entries)
            {
                if (!TryGetValidDefinition(entry, catalog, out _)) continue;
                totalWeight += entry.Weight;
            }

            return totalWeight;
        }

        private static bool TryGetValidDefinition(
            ExtractionLootTableEntry entry,
            IExtractionItemCatalog catalog,
            out ExtractionItemDefinition definition)
        {
            if (entry == null || !entry.IsValid)
            {
                definition = null;
                return false;
            }

            return catalog.TryGetItemDefinition(entry.DefinitionId, out definition) && definition != null;
        }

        private static long NormalizeRoll(int rollValue, long totalWeight)
        {
            long result = rollValue % totalWeight;
            return result < 0 ? result + totalWeight : result;
        }
    }
}
