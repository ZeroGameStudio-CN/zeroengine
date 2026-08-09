using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionBlueprintTransactionService
    {
        public static bool TryLearnBlueprint(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            ExtractionBlueprintDefinition blueprint,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> costSourceContainers)
        {
            if (!CanLearnBlueprint(profile, blueprint, itemCatalog, out var costs)) return false;
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, costSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, costSourceContainers, out var costReceipt)) return false;

            if (ExtractionBlueprintService.TryUnlockBlueprint(profile, config, blueprint.BlueprintId)) return true;

            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }

        private static bool CanLearnBlueprint(
            ExtractionProfileSaveData profile,
            ExtractionBlueprintDefinition blueprint,
            IExtractionItemCatalog itemCatalog,
            out List<ExtractionItemCost> costs)
        {
            costs = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || blueprint == null || !blueprint.IsValid || itemCatalog == null) return false;

            profile.EnsureInitialized();
            if (!itemCatalog.TryGetItemDefinition(blueprint.CostItemDefinitionId, out _)) return false;
            if (ExtractionBlueprintService.IsBlueprintUnlocked(profile, blueprint.BlueprintId)) return false;

            costs = new List<ExtractionItemCost>
            {
                new ExtractionItemCost(blueprint.CostItemDefinitionId, blueprint.CostQuantity)
            };
            return true;
        }
    }
}
