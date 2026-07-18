using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMerchantOfferDefinition
    {
        public const string BloodSampleResourceId = "BloodSample";

        public string OfferId;
        public string ItemDefinitionId;
        public int Quantity;
        public string PriceItemDefinitionId;
        public string PriceResourceId;
        public int PriceQuantity;
        public int InitialStock = 1;
        public int SelectionWeight = 1;
        public bool IsReliefOffer;
        public int RequiredMerchantLevel;
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        public bool IsValid =>
            !string.IsNullOrEmpty(OfferId)
            && !string.IsNullOrEmpty(ItemDefinitionId)
            && Quantity > 0
            && (!string.IsNullOrEmpty(PriceItemDefinitionId)
                || PriceResourceId == BloodSampleResourceId)
            && PriceQuantity > 0
            && InitialStock > 0
            && SelectionWeight > 0
            && RequiredMerchantLevel > 0;

        public bool UsesSharedBloodSample => PriceResourceId == BloodSampleResourceId;

        public ExtractionMerchantOfferDefinition(
            string offerId,
            string itemDefinitionId,
            int quantity,
            string priceItemDefinitionId,
            int priceQuantity,
            int requiredMerchantLevel)
        {
            OfferId = offerId;
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
            PriceItemDefinitionId = priceItemDefinitionId;
            PriceQuantity = priceQuantity;
            RequiredMerchantLevel = requiredMerchantLevel;
        }
    }
}
