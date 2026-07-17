using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionBuybackQuoteDefinition
    {
        public const string DefaultCostItemDefinitionId = "buyback-token";

        public string ItemDefinitionId;
        public string CostItemDefinitionId;
        public int CostPerUnit;
        public int MinimumCost;

        public bool IsValid =>
            !string.IsNullOrEmpty(ItemDefinitionId)
            && !string.IsNullOrEmpty(CostItemDefinitionId)
            && CostPerUnit > 0
            && MinimumCost >= 0;

        public ExtractionBuybackQuoteDefinition(
            string itemDefinitionId,
            int costPerUnit,
            int minimumCost)
            : this(itemDefinitionId, DefaultCostItemDefinitionId, costPerUnit, minimumCost)
        {
        }

        public ExtractionBuybackQuoteDefinition(
            string itemDefinitionId,
            string costItemDefinitionId,
            int costPerUnit,
            int minimumCost)
        {
            ItemDefinitionId = itemDefinitionId;
            CostItemDefinitionId = costItemDefinitionId;
            CostPerUnit = costPerUnit;
            MinimumCost = minimumCost;
        }
    }

    public class ExtractionBuybackQuote
    {
        public string ItemInstanceId;
        public string ItemDefinitionId;
        public int Quantity;
        public string CostItemDefinitionId;
        public int Cost;

        public ExtractionBuybackQuote(
            string itemInstanceId,
            string itemDefinitionId,
            int quantity,
            int cost)
            : this(
                itemInstanceId,
                itemDefinitionId,
                quantity,
                ExtractionBuybackQuoteDefinition.DefaultCostItemDefinitionId,
                cost)
        {
        }

        public ExtractionBuybackQuote(
            string itemInstanceId,
            string itemDefinitionId,
            int quantity,
            string costItemDefinitionId,
            int cost)
        {
            ItemInstanceId = itemInstanceId;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
            CostItemDefinitionId = costItemDefinitionId;
            Cost = cost;
        }
    }
}
