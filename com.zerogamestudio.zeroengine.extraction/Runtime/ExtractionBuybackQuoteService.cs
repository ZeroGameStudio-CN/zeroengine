using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionBuybackQuoteService
    {
        public static bool TryGetQuote(
            ExtractionProfileSaveData profile,
            List<ExtractionBuybackQuoteDefinition> quoteDefinitions,
            string itemInstanceId,
            out ExtractionBuybackQuote quote)
        {
            quote = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || quoteDefinitions == null || string.IsNullOrEmpty(itemInstanceId)) return false;
            if (!profile.Items.TryGet(itemInstanceId, out var item)) return false;
            if (item == null || item.Quantity <= 0) return false;
            if (!profile.Ownership.TryGetContainer(itemInstanceId, out var container)) return false;
            if (container != ExtractionInventoryContainerType.Lost) return false;
            if (!profile.Recovery.TryGetState(itemInstanceId, out var state)) return false;
            if (state != ExtractionRecoveryItemState.Recoverable) return false;
            if (!TryGetQuoteDefinition(quoteDefinitions, item.DefinitionId, out var definition)) return false;

            long raw = (long)definition.CostPerUnit * item.Quantity;
            if (raw < definition.MinimumCost) raw = definition.MinimumCost;
            if (raw > int.MaxValue) raw = int.MaxValue;
            int cost = (int)raw;

            quote = new ExtractionBuybackQuote(
                item.InstanceId,
                item.DefinitionId,
                item.Quantity,
                definition.CostItemDefinitionId,
                cost);
            return true;
        }

        private static bool TryGetQuoteDefinition(
            List<ExtractionBuybackQuoteDefinition> quoteDefinitions,
            string itemDefinitionId,
            out ExtractionBuybackQuoteDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(itemDefinitionId)) return false;

            foreach (var candidate in quoteDefinitions)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.ItemDefinitionId != itemDefinitionId) continue;

                definition = candidate;
                return true;
            }

            return false;
        }
    }
}
