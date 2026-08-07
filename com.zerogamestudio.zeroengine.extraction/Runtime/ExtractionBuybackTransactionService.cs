using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionBuybackTransactionService
    {
        public static bool TryCompleteBuyback(
            ExtractionProfileSaveData profile,
            List<ExtractionBuybackQuoteDefinition> quoteDefinitions,
            string itemInstanceId,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> costSourceContainers)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || itemCatalog == null) return false;
            if (!ExtractionBuybackQuoteService.TryGetQuote(
                    profile,
                    quoteDefinitions,
                    itemInstanceId,
                    out var quote))
            {
                return false;
            }

            if (string.IsNullOrEmpty(quote.CostItemDefinitionId)) return false;
            if (!itemCatalog.TryGetItemDefinition(quote.CostItemDefinitionId, out _)) return false;

            var costs = new List<ExtractionItemCost>
            {
                new ExtractionItemCost(quote.CostItemDefinitionId, quote.Cost)
            };
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, costSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, costSourceContainers, out var costReceipt)) return false;

            if (ExtractionRecoveryService.TryRecoverBuybackItem(profile, itemInstanceId)) return true;

            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }
    }
}
