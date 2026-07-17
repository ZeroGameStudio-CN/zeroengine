using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMerchantOfferService
    {
        public static bool TryGetAvailableOffers(
            ExtractionProfileSaveData profile,
            List<ExtractionMerchantOfferDefinition> offers,
            IExtractionItemCatalog itemCatalog,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionMerchantOfferDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || offers == null || itemCatalog == null) return false;
            profile.EnsureInitialized();

            int merchantLevel = profile.Facilities.GetLevel(ExtractionFacilityType.Merchant);
            foreach (var offer in offers)
            {
                if (offer == null || !offer.IsValid) continue;
                if (merchantLevel < offer.RequiredMerchantLevel) continue;
                if (!HasCatalogItems(offer, itemCatalog)) continue;
                if (!MeetsMetaRequirements(offer, metaProgress)) continue;

                results.Add(offer);
            }

            return results.Count > 0;
        }

        private static bool HasCatalogItems(
            ExtractionMerchantOfferDefinition offer,
            IExtractionItemCatalog itemCatalog)
        {
            return itemCatalog.TryGetItemDefinition(offer.ItemDefinitionId, out _)
                && itemCatalog.TryGetItemDefinition(offer.PriceItemDefinitionId, out _);
        }

        private static bool MeetsMetaRequirements(
            ExtractionMerchantOfferDefinition offer,
            IExtractionMetaProgressProvider metaProgress)
        {
            metaProgress ??= new ExtractionNoOpMetaProgressProvider();

            if (!string.IsNullOrEmpty(offer.RequiredSharedUnlockId))
            {
                if (offer.RequiredSharedUnlockLevel <= 0) return false;
                if (metaProgress.GetSharedUnlockLevel(offer.RequiredSharedUnlockId) < offer.RequiredSharedUnlockLevel)
                    return false;
            }

            if (!string.IsNullOrEmpty(offer.RequiredSharedTalentId))
            {
                if (!metaProgress.HasSharedTalent(offer.RequiredSharedTalentId)) return false;
            }

            return true;
        }
    }
}
