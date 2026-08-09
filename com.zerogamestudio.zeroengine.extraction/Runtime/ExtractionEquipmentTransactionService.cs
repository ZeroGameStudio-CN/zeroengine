using System;

namespace POB.Extraction
{
    public enum ExtractionEquipmentTransactionResult
    {
        Succeeded = 0,
        AlreadyApplied = 1,
        InvalidRequest = 2,
        ItemNotFound = 3,
        PolicyDenied = 4,
        WrongSlot = 5,
        LocationConflict = 6,
        NoSpace = 7,
        CommitFailed = 8
    }

    public static class ExtractionEquipmentTransactionService
    {
        public static bool TryEquip(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            ExtractionInventoryContainerType sourceContainer,
            string equipmentLocationSubtype,
            string slotId,
            ExtractionEquipmentSlotType slotType,
            string receiptId,
            out string displacedItemInstanceId,
            out ExtractionEquipmentTransactionResult result)
        {
            displacedItemInstanceId = null;
            result = ExtractionEquipmentTransactionResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || itemCatalog == null
                || string.IsNullOrEmpty(itemInstanceId)
                || string.IsNullOrEmpty(slotId)
                || string.IsNullOrEmpty(receiptId)
                || !Enum.IsDefined(typeof(ExtractionEquipmentSlotType), slotType)
                || slotType == ExtractionEquipmentSlotType.None
                || !IsSourceAllowed(sourceContainer, equipmentLocationSubtype))
            {
                return false;
            }

            profile.EnsureInitialized();
            raidInventory?.EnsureInitialized();
            if (!ExtractionItemLocationService.TryGetEquipment(
                    profile,
                    raidInventory,
                    equipmentLocationSubtype,
                    out var equipment)
                || !ExtractionItemLocationService.TryGetGrid(
                    profile,
                    raidInventory,
                    sourceContainer,
                    out var sourceGrid))
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            equipment.EnsureInitialized();
            if (equipment.AppliedReceiptIds.Contains(receiptId))
            {
                result = ExtractionEquipmentTransactionResult.AlreadyApplied;
                return true;
            }
            if (!profile.Items.TryGet(itemInstanceId, out var item)
                || !itemCatalog.TryGetItemDefinition(item.DefinitionId, out var definition))
            {
                result = ExtractionEquipmentTransactionResult.ItemNotFound;
                return false;
            }

            var policy = ExtractionItemActionPolicyService.GetPolicy(definition);
            if (policy == null
                || !policy.CanEquip
                || string.IsNullOrEmpty(policy.EffectAdapterId))
            {
                result = ExtractionEquipmentTransactionResult.PolicyDenied;
                return false;
            }
            if (policy.EquipmentSlotType != slotType
                || !SlotIdMatchesType(slotId, slotType))
            {
                result = ExtractionEquipmentTransactionResult.WrongSlot;
                return false;
            }
            if (!sourceGrid.TryGetPlacement(itemInstanceId, out var sourcePlacement)
                || !TryFindOwnership(profile, itemInstanceId, out var sourceEntry)
                || sourceEntry.Container != sourceContainer)
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }
            if (equipment.TryGetSlotForItem(itemInstanceId, out _))
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            equipment.TryGetItem(slotId, out displacedItemInstanceId);
            ExtractionItemInstance displacedItem = null;
            ExtractionItemDefinition displacedDefinition = null;
            int displacedX = 0;
            int displacedY = 0;
            bool displacedRotated = false;
            if (!string.IsNullOrEmpty(displacedItemInstanceId))
            {
                if (!profile.Items.TryGet(displacedItemInstanceId, out displacedItem)
                    || !itemCatalog.TryGetItemDefinition(displacedItem.DefinitionId, out displacedDefinition)
                    || !TryFindOwnership(profile, displacedItemInstanceId, out var displacedEntry)
                    || displacedEntry.Container != ExtractionInventoryContainerType.EquipmentSlot
                    || displacedEntry.LocationSubtype != equipmentLocationSubtype
                    || displacedEntry.LocationId != slotId)
                {
                    result = ExtractionEquipmentTransactionResult.LocationConflict;
                    return false;
                }

                var probe = CloneGrid(sourceGrid);
                if (!probe.TryRemove(itemInstanceId)
                    || !probe.TryFindFreeSlotWithRotation(
                        displacedDefinition.Width,
                        displacedDefinition.Height,
                        displacedDefinition.CanRotate,
                        out displacedX,
                        out displacedY,
                        out displacedRotated))
                {
                    result = ExtractionEquipmentTransactionResult.NoSpace;
                    return false;
                }
            }

            if (!sourceGrid.TryRemove(itemInstanceId))
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }
            if (displacedItem != null
                && !sourceGrid.TryPlace(
                    displacedItem,
                    displacedDefinition,
                    displacedX,
                    displacedY,
                    displacedRotated))
            {
                sourceGrid.TryPlace(
                    item,
                    definition,
                    sourcePlacement.X,
                    sourcePlacement.Y,
                    sourcePlacement.Rotated);
                result = ExtractionEquipmentTransactionResult.NoSpace;
                return false;
            }

            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    sourceContainer,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    equipmentLocationSubtype,
                    slotId))
            {
                RollbackSourceGrid(
                    sourceGrid,
                    item,
                    definition,
                    sourcePlacement,
                    displacedItemInstanceId);
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }
            if (displacedItem != null
                && !profile.Ownership.TryMove(
                    displacedItemInstanceId,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    sourceContainer))
            {
                profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    sourceContainer);
                RollbackSourceGrid(
                    sourceGrid,
                    item,
                    definition,
                    sourcePlacement,
                    displacedItemInstanceId);
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            if (!equipment.TrySet(slotId, itemInstanceId))
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }
            var slot = FindSlot(equipment, slotId);
            if (slot != null) slot.EffectReceiptId = receiptId;
            equipment.AppliedReceiptIds.Add(receiptId);
            result = ExtractionEquipmentTransactionResult.Succeeded;
            return true;
        }

        public static bool TryUnequip(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string equipmentLocationSubtype,
            string slotId,
            ExtractionInventoryContainerType targetContainer,
            string receiptId,
            out string itemInstanceId,
            out ExtractionEquipmentTransactionResult result)
        {
            itemInstanceId = null;
            result = ExtractionEquipmentTransactionResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || itemCatalog == null
                || string.IsNullOrEmpty(slotId)
                || string.IsNullOrEmpty(receiptId)
                || !IsSourceAllowed(targetContainer, equipmentLocationSubtype))
            {
                return false;
            }

            profile.EnsureInitialized();
            raidInventory?.EnsureInitialized();
            if (!ExtractionItemLocationService.TryGetEquipment(
                    profile,
                    raidInventory,
                    equipmentLocationSubtype,
                    out var equipment)
                || !ExtractionItemLocationService.TryGetGrid(
                    profile,
                    raidInventory,
                    targetContainer,
                    out var targetGrid))
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            equipment.EnsureInitialized();
            if (equipment.AppliedReceiptIds.Contains(receiptId))
            {
                result = ExtractionEquipmentTransactionResult.AlreadyApplied;
                return true;
            }
            if (!equipment.TryGetItem(slotId, out itemInstanceId)
                || !profile.Items.TryGet(itemInstanceId, out var item)
                || !itemCatalog.TryGetItemDefinition(item.DefinitionId, out var definition)
                || !TryFindOwnership(profile, itemInstanceId, out var entry)
                || entry.Container != ExtractionInventoryContainerType.EquipmentSlot
                || entry.LocationSubtype != equipmentLocationSubtype
                || entry.LocationId != slotId)
            {
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            if (!targetGrid.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated)
                || !targetGrid.TryPlace(item, definition, x, y, rotated))
            {
                result = ExtractionEquipmentTransactionResult.NoSpace;
                return false;
            }
            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    targetContainer)
                || !equipment.TryClear(slotId, itemInstanceId))
            {
                targetGrid.TryRemove(itemInstanceId);
                profile.Ownership.TryMove(
                    itemInstanceId,
                    targetContainer,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    equipmentLocationSubtype,
                    slotId);
                result = ExtractionEquipmentTransactionResult.LocationConflict;
                return false;
            }

            equipment.AppliedReceiptIds.Add(receiptId);
            result = ExtractionEquipmentTransactionResult.Succeeded;
            return true;
        }

        private static bool IsSourceAllowed(
            ExtractionInventoryContainerType container,
            string equipmentLocationSubtype)
        {
            if (equipmentLocationSubtype == ExtractionItemLocationService.BaseEquipmentLocationSubtype)
            {
                return container == ExtractionInventoryContainerType.Stash
                       || container == ExtractionInventoryContainerType.Loadout;
            }

            return equipmentLocationSubtype == ExtractionItemLocationService.RaidEquipmentLocationSubtype
                   && container == ExtractionInventoryContainerType.RaidBackpack;
        }

        private static bool SlotIdMatchesType(
            string slotId,
            ExtractionEquipmentSlotType slotType)
        {
            string prefix = slotType switch
            {
                ExtractionEquipmentSlotType.Weapon => "weapon",
                ExtractionEquipmentSlotType.Relic => "relic",
                ExtractionEquipmentSlotType.Card => "card",
                _ => null
            };
            return prefix != null && slotId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
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

        private static ExtractionEquipmentSlotState FindSlot(
            ExtractionEquipmentState equipment,
            string slotId)
        {
            foreach (var slot in equipment.Slots)
            {
                if (slot?.SlotId == slotId) return slot;
            }
            return null;
        }

        private static ExtractionItemGrid CloneGrid(ExtractionItemGrid source)
        {
            var clone = new ExtractionItemGrid(source.Width, source.Height);
            foreach (var placement in source.Placements)
            {
                if (placement == null) continue;
                clone.Placements.Add(new ExtractionItemPlacement(
                    placement.ItemInstanceId,
                    placement.X,
                    placement.Y,
                    placement.Width,
                    placement.Height,
                    placement.Rotated));
            }
            return clone;
        }

        private static void RollbackSourceGrid(
            ExtractionItemGrid sourceGrid,
            ExtractionItemInstance sourceItem,
            ExtractionItemDefinition sourceDefinition,
            ExtractionItemPlacement sourcePlacement,
            string displacedItemInstanceId)
        {
            if (!string.IsNullOrEmpty(displacedItemInstanceId))
                sourceGrid.TryRemove(displacedItemInstanceId);
            sourceGrid.TryPlace(
                sourceItem,
                sourceDefinition,
                sourcePlacement.X,
                sourcePlacement.Y,
                sourcePlacement.Rotated);
        }
    }
}
