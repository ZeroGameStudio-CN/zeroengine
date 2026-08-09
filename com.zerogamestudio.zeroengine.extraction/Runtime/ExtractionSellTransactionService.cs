using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionSellTransactionService
    {
        public const float SellValueMultiplier = 0.5f;

        public static bool TrySellItem(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            List<ExtractionInventoryContainerType> sourceContainers,
            IExtractionItemCatalog itemCatalog,
            string proceedsItemDefinitionId,
            string grantedProceedsInstanceId,
            ExtractionInventoryContainerType proceedsTargetContainer,
            int x,
            int y,
            bool rotated,
            out int grantedQuantity)
        {
            grantedQuantity = 0;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || itemCatalog == null) return false;
            if (string.IsNullOrEmpty(itemInstanceId) || string.IsNullOrEmpty(grantedProceedsInstanceId)) return false;
            if (sourceContainers == null || sourceContainers.Count == 0) return false;
            profile.EnsureInitialized();

            if (!profile.Items.TryGet(itemInstanceId, out var soldItem)) return false;
            if (!itemCatalog.TryGetItemDefinition(soldItem.DefinitionId, out var soldDefinition)) return false;
            if (!ExtractionItemActionPolicyService.CanSell(soldDefinition, soldItem)) return false;
            if (!profile.Ownership.TryGetContainer(itemInstanceId, out var sourceContainer)) return false;
            if (!sourceContainers.Contains(sourceContainer)) return false;
            if (!TryGetGrid(profile, sourceContainer, out var sourceGrid)) return false;
            if (!sourceGrid.TryGetPlacement(itemInstanceId, out var soldPlacement)) return false;
            if (!itemCatalog.TryGetItemDefinition(proceedsItemDefinitionId, out var proceedsDefinition)) return false;
            if (!TryGetGrid(profile, proceedsTargetContainer, out var targetGrid)) return false;
            if (profile.Items.TryGet(grantedProceedsInstanceId, out _)) return false;
            if (profile.Ownership.TryGetContainer(grantedProceedsInstanceId, out _)) return false;

            // Value 是每单位基准单价（见 ExtractionItemDefinition 注释），出售按 Quantity 线性折算；
            // 半价后不足 1 视为不可出售，不生成 0 数量的收益物。
            grantedQuantity = (int)(soldDefinition.Value * soldItem.Quantity * SellValueMultiplier);
            if (grantedQuantity <= 0) return false;

            var grantedItem = new ExtractionItemInstance(grantedProceedsInstanceId, proceedsItemDefinitionId, grantedQuantity);
            if (!CanPlace(targetGrid, grantedItem, proceedsDefinition, x, y, rotated)) return false;

            if (!sourceGrid.TryRemove(itemInstanceId)) return false;
            if (!profile.Items.TryRemove(itemInstanceId))
            {
                sourceGrid.TryPlace(soldItem, soldDefinition, soldPlacement.X, soldPlacement.Y, soldPlacement.Rotated);
                return false;
            }
            if (!profile.Ownership.TryRemove(itemInstanceId))
            {
                profile.Items.Register(soldItem);
                sourceGrid.TryPlace(soldItem, soldDefinition, soldPlacement.X, soldPlacement.Y, soldPlacement.Rotated);
                return false;
            }

            if (!profile.Items.Register(grantedItem))
            {
                RestoreSoldItem(profile, soldItem, soldDefinition, sourceGrid, sourceContainer, soldPlacement);
                return false;
            }
            if (!targetGrid.TryPlace(grantedItem, proceedsDefinition, x, y, rotated))
            {
                profile.Items.TryRemove(grantedProceedsInstanceId);
                RestoreSoldItem(profile, soldItem, soldDefinition, sourceGrid, sourceContainer, soldPlacement);
                return false;
            }
            if (!profile.Ownership.Register(grantedProceedsInstanceId, proceedsTargetContainer))
            {
                targetGrid.TryRemove(grantedProceedsInstanceId);
                profile.Items.TryRemove(grantedProceedsInstanceId);
                RestoreSoldItem(profile, soldItem, soldDefinition, sourceGrid, sourceContainer, soldPlacement);
                return false;
            }

            return true;
        }

        private static void RestoreSoldItem(
            ExtractionProfileSaveData profile,
            ExtractionItemInstance soldItem,
            ExtractionItemDefinition soldDefinition,
            ExtractionItemGrid sourceGrid,
            ExtractionInventoryContainerType sourceContainer,
            ExtractionItemPlacement soldPlacement)
        {
            profile.Items.Register(soldItem);
            sourceGrid.TryPlace(soldItem, soldDefinition, soldPlacement.X, soldPlacement.Y, soldPlacement.Rotated);
            profile.Ownership.Register(soldItem.InstanceId, sourceContainer);
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
