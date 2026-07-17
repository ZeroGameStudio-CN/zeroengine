using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMerchantPurchaseTransactionService
    {
        public static bool TryPurchaseOffer(
            ExtractionProfileSaveData profile,
            ExtractionMerchantOfferDefinition offer,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> priceSourceContainers,
            ExtractionInventoryContainerType targetContainer,
            string purchasedItemInstanceId,
            int x,
            int y,
            bool rotated)
        {
            if (!CanPurchaseOffer(profile, offer, itemCatalog, purchasedItemInstanceId, out var purchasedDefinition))
                return false;
            if (!TryGetGrid(profile, targetContainer, out var targetGrid)) return false;

            var purchasedItem = new ExtractionItemInstance(
                purchasedItemInstanceId,
                offer.ItemDefinitionId,
                offer.Quantity);
            if (!CanPlace(targetGrid, purchasedItem, purchasedDefinition, x, y, rotated)) return false;

            var costs = new List<ExtractionItemCost>
            {
                new ExtractionItemCost(offer.PriceItemDefinitionId, offer.PriceQuantity)
            };
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, priceSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, priceSourceContainers, out var costReceipt)) return false;

            if (!profile.Items.Register(purchasedItem))
            {
                ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
                return false;
            }
            if (!targetGrid.TryPlace(purchasedItem, purchasedDefinition, x, y, rotated))
            {
                profile.Items.TryRemove(purchasedItemInstanceId);
                ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
                return false;
            }

            if (profile.Ownership.Register(purchasedItemInstanceId, targetContainer)) return true;

            targetGrid.TryRemove(purchasedItemInstanceId);
            profile.Items.TryRemove(purchasedItemInstanceId);
            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }

        private static bool CanPurchaseOffer(
            ExtractionProfileSaveData profile,
            ExtractionMerchantOfferDefinition offer,
            IExtractionItemCatalog itemCatalog,
            string purchasedItemInstanceId,
            out ExtractionItemDefinition purchasedDefinition)
        {
            purchasedDefinition = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || offer == null || !offer.IsValid || itemCatalog == null) return false;
            if (string.IsNullOrEmpty(purchasedItemInstanceId)) return false;
            profile.EnsureInitialized();
            if (profile.Facilities.GetLevel(ExtractionFacilityType.Merchant) < offer.RequiredMerchantLevel)
                return false;
            if (profile.Items.TryGet(purchasedItemInstanceId, out _)) return false;
            if (profile.Ownership.TryGetContainer(purchasedItemInstanceId, out _)) return false;
            if (!itemCatalog.TryGetItemDefinition(offer.ItemDefinitionId, out purchasedDefinition)) return false;
            if (!itemCatalog.TryGetItemDefinition(offer.PriceItemDefinitionId, out _)) return false;

            return true;
        }

        private static bool CanPlace(
            ExtractionItemGrid grid,
            ExtractionItemInstance item,
            ExtractionItemDefinition definition,
            int x,
            int y,
            bool rotated)
        {
            if (!grid.TryPlace(item, definition, x, y, rotated)) return false;
            grid.TryRemove(item.InstanceId);
            return true;
        }

        private static bool TryGetGrid(
            ExtractionProfileSaveData profile,
            ExtractionInventoryContainerType container,
            out ExtractionItemGrid grid)
        {
            grid = container switch
            {
                ExtractionInventoryContainerType.Stash => profile.Stash,
                ExtractionInventoryContainerType.Loadout => profile.Loadout,
                ExtractionInventoryContainerType.SecureContainer => profile.SecureContainer,
                ExtractionInventoryContainerType.Holding => profile.RecoveryHolding,
                _ => null
            };

            return grid != null;
        }
    }
}
