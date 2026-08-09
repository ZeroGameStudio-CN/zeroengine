using System;

namespace POB.Extraction
{
    public enum ExtractionItemUseResult
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        InvalidRequest = 2,
        ItemNotFound = 3,
        PolicyDenied = 4,
        Exhausted = 5,
        LocationConflict = 6,
        CommitFailed = 7
    }

    public static class ExtractionItemUseService
    {
        public static bool TryConsumeForUse(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            string receiptId,
            out string useActionId,
            out ExtractionItemUseResult result)
        {
            useActionId = null;
            result = ExtractionItemUseResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || itemCatalog == null
                || string.IsNullOrEmpty(itemInstanceId)
                || string.IsNullOrEmpty(receiptId))
            {
                return false;
            }

            profile.EnsureInitialized();
            if (!profile.Items.TryGet(itemInstanceId, out var item)
                || !itemCatalog.TryGetItemDefinition(item.DefinitionId, out var definition))
            {
                result = ExtractionItemUseResult.ItemNotFound;
                return false;
            }

            var policy = ExtractionItemActionPolicyService.GetPolicy(definition);
            useActionId = policy?.UseActionId;
            if (profile.ItemActionReceiptIds.Contains(receiptId))
            {
                result = ExtractionItemUseResult.AlreadyApplied;
                return true;
            }
            if (!ExtractionItemActionPolicyService.CanUse(definition))
            {
                result = ExtractionItemUseResult.PolicyDenied;
                return false;
            }

            if (!TryFindOwnership(profile, itemInstanceId, out var entry))
            {
                result = ExtractionItemUseResult.LocationConflict;
                return false;
            }
            if (!IsUsableLocation(entry))
            {
                result = ExtractionItemUseResult.LocationConflict;
                return false;
            }

            switch (policy.ConsumptionType)
            {
                case ExtractionItemConsumptionType.None:
                    break;
                case ExtractionItemConsumptionType.Quantity:
                    if (item.Quantity <= 0)
                    {
                        result = ExtractionItemUseResult.Exhausted;
                        return false;
                    }
                    item.Quantity--;
                    if (item.Quantity == 0
                        && !TryMoveToTerminal(
                            profile,
                            raidInventory,
                            item,
                            definition,
                            entry,
                            ExtractionInventoryContainerType.Consumed))
                    {
                        item.Quantity++;
                        result = ExtractionItemUseResult.LocationConflict;
                        return false;
                    }
                    break;
                case ExtractionItemConsumptionType.Durability:
                    if (item.CurrentDurability <= 0)
                    {
                        result = ExtractionItemUseResult.Exhausted;
                        return false;
                    }
                    item.CurrentDurability--;
                    if (item.CurrentDurability == 0
                        && !TryMoveToTerminal(
                            profile,
                            raidInventory,
                            item,
                            definition,
                            entry,
                            ExtractionInventoryContainerType.DestroyedByUse))
                    {
                        item.CurrentDurability++;
                        result = ExtractionItemUseResult.LocationConflict;
                        return false;
                    }
                    break;
                case ExtractionItemConsumptionType.DestroyInstance:
                    if (!TryMoveToTerminal(
                            profile,
                            raidInventory,
                            item,
                            definition,
                            entry,
                            ExtractionInventoryContainerType.DestroyedByUse))
                    {
                        result = ExtractionItemUseResult.LocationConflict;
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            profile.ItemActionReceiptIds.Add(receiptId);
            result = ExtractionItemUseResult.Succeeded;
            return true;
        }

        private static bool TryMoveToTerminal(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            ExtractionItemInstance item,
            ExtractionItemDefinition definition,
            ExtractionOwnershipEntry entry,
            ExtractionInventoryContainerType terminal)
        {
            Action restore = () => { };
            if (entry.Container == ExtractionInventoryContainerType.EquipmentSlot)
            {
                if (!ExtractionItemLocationService.TryGetEquipment(
                        profile,
                        raidInventory,
                        entry.LocationSubtype,
                        out var equipment)
                    || !equipment.TryClear(entry.LocationId, item.InstanceId))
                {
                    return false;
                }
                restore = () => equipment.TrySet(entry.LocationId, item.InstanceId);
            }
            else if (ExtractionItemLocationService.TryGetGrid(
                         profile,
                         raidInventory,
                         entry.Container,
                         out var grid))
            {
                if (!grid.TryGetPlacement(item.InstanceId, out var placement)
                    || !grid.TryRemove(item.InstanceId))
                {
                    return false;
                }
                restore = () => grid.TryPlace(item, definition, placement.X, placement.Y, placement.Rotated);
            }

            var source = entry.Container;
            if (profile.Ownership.TryMove(
                    item.InstanceId,
                    source,
                    terminal,
                    "use",
                    null))
            {
                return true;
            }

            restore();
            return false;
        }

        private static bool TryFindOwnership(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            out ExtractionOwnershipEntry entry)
        {
            entry = null;
            foreach (var candidate in profile.Ownership.Entries)
            {
                if (candidate?.ItemInstanceId != itemInstanceId) continue;
                if (entry != null) return false;
                entry = candidate;
            }
            return entry != null;
        }

        private static bool IsUsableLocation(ExtractionOwnershipEntry entry)
        {
            if (entry == null) return false;
            return entry.Container == ExtractionInventoryContainerType.Stash
                   || entry.Container == ExtractionInventoryContainerType.Loadout
                   || entry.Container == ExtractionInventoryContainerType.SecureContainer
                   || entry.Container == ExtractionInventoryContainerType.Holding
                   || entry.Container == ExtractionInventoryContainerType.InRaid
                   || entry.Container == ExtractionInventoryContainerType.RaidBackpack
                   || entry.Container == ExtractionInventoryContainerType.InSecureContainer
                   || entry.Container == ExtractionInventoryContainerType.EquipmentSlot;
        }
    }
}
