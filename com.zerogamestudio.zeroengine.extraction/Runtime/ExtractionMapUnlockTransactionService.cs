using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMapUnlockTransactionService
    {
        public static bool TryUnlockMapWithCost(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string mapId,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> costSourceContainers)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || config == null || string.IsNullOrEmpty(mapId)) return false;
            if (!config.TryGetMap(mapId, out var map) || map == null || !map.IsValid) return false;

            profile.EnsureInitialized();
            if (ExtractionMapAccessService.IsMapUnlocked(profile, mapId)) return true;

            var costs = map.UnlockCosts;
            if (costs == null || costs.Count == 0)
                return ExtractionMapAccessService.TryUnlockMap(profile, config, mapId);

            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, costSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, costSourceContainers, out var costReceipt)) return false;

            if (ExtractionMapAccessService.TryUnlockMap(profile, config, mapId)) return true;

            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }
    }
}
