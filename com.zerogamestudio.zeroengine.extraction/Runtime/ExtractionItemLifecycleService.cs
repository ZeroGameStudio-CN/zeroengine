using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionItemLifecycleResult
    {
        Succeeded = 0,
        Cancelled = 1,
        InvalidRequest = 2,
        ItemNotFound = 3,
        PolicyDenied = 4,
        NoSpace = 5,
        LocationConflict = 6,
        CommitFailed = 7
    }

    public static class ExtractionItemLifecycleService
    {
        public static bool TryDropInRaid(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            string worldPickupId,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                result = ExtractionItemLifecycleResult.ItemNotFound;
                return false;
            }

            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(worldPickupId)
                || !ExtractionItemActionPolicyService.CanDrop(definition, item))
            {
                result = ExtractionItemLifecycleResult.PolicyDenied;
                return false;
            }
            if (entry.Container != ExtractionInventoryContainerType.RaidBackpack
                && (entry.Container != ExtractionInventoryContainerType.EquipmentSlot
                    || entry.LocationSubtype != ExtractionItemLocationService.RaidEquipmentLocationSubtype))
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (!TryDetach(profile, raidInventory, item, definition, entry, out var restore))
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }
            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    entry.Container,
                    ExtractionInventoryContainerType.WorldPickup,
                    "world-pickup",
                    worldPickupId))
            {
                restore();
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            var worldItems = profile.ActiveRaid?.Content?.WorldPickupItemInstanceIds;
            if (worldItems != null && !worldItems.Contains(itemInstanceId)) worldItems.Add(itemInstanceId);
            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TryPickupWorldItem(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            ExtractionInventoryContainerType targetContainer,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || raidInventory == null
                || (targetContainer != ExtractionInventoryContainerType.RaidBackpack
                    && targetContainer != ExtractionInventoryContainerType.InSecureContainer)
                || !TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                return false;
            }
            if (entry.Container != ExtractionInventoryContainerType.WorldPickup)
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (targetContainer == ExtractionInventoryContainerType.InSecureContainer
                && !ExtractionItemActionPolicyService.CanPlaceInSecure(definition))
            {
                result = ExtractionItemLifecycleResult.PolicyDenied;
                return false;
            }

            if (!ExtractionItemLocationService.TryGetGrid(profile, raidInventory, targetContainer, out var targetGrid)
                || !targetGrid.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated)
                || !targetGrid.TryPlace(item, definition, x, y, rotated))
            {
                result = ExtractionItemLifecycleResult.NoSpace;
                return false;
            }

            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.WorldPickup,
                    targetContainer))
            {
                targetGrid.TryRemove(itemInstanceId);
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            profile.ActiveRaid?.Content?.WorldPickupItemInstanceIds?.Remove(itemInstanceId);
            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TryMoveRaidCarriedItem(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            ExtractionInventoryContainerType targetContainer,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || raidInventory == null
                || (targetContainer != ExtractionInventoryContainerType.RaidBackpack
                    && targetContainer != ExtractionInventoryContainerType.InSecureContainer)
                || !TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                return false;
            }

            if ((entry.Container != ExtractionInventoryContainerType.RaidBackpack
                 && entry.Container != ExtractionInventoryContainerType.InSecureContainer)
                || entry.Container == targetContainer)
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (targetContainer == ExtractionInventoryContainerType.InSecureContainer
                && !ExtractionItemActionPolicyService.CanPlaceInSecure(definition))
            {
                result = ExtractionItemLifecycleResult.PolicyDenied;
                return false;
            }

            if (!ExtractionItemLocationService.TryGetGrid(
                    profile,
                    raidInventory,
                    entry.Container,
                    out var sourceGrid)
                || !ExtractionItemLocationService.TryGetGrid(
                    profile,
                    raidInventory,
                    targetContainer,
                    out var targetGrid)
                || !sourceGrid.TryGetPlacement(itemInstanceId, out var sourcePlacement)
                || !targetGrid.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated)
                || !targetGrid.TryPlace(item, definition, x, y, rotated))
            {
                result = ExtractionItemLifecycleResult.NoSpace;
                return false;
            }

            if (!sourceGrid.TryRemove(itemInstanceId))
            {
                targetGrid.TryRemove(itemInstanceId);
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (!profile.Ownership.TryMove(itemInstanceId, entry.Container, targetContainer))
            {
                targetGrid.TryRemove(itemInstanceId);
                sourceGrid.TryPlace(
                    item,
                    definition,
                    sourcePlacement.X,
                    sourcePlacement.Y,
                    sourcePlacement.Rotated);
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TryMoveBaseStoredItem(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            ExtractionInventoryContainerType targetContainer,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || (targetContainer != ExtractionInventoryContainerType.Stash
                    && targetContainer != ExtractionInventoryContainerType.Loadout
                    && targetContainer != ExtractionInventoryContainerType.SecureContainer)
                || !TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                return false;
            }

            if (!IsBaseGridLocation(entry.Container) || entry.Container == targetContainer)
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }
            if (targetContainer == ExtractionInventoryContainerType.SecureContainer
                && !ExtractionItemActionPolicyService.CanPlaceInSecure(definition))
            {
                result = ExtractionItemLifecycleResult.PolicyDenied;
                return false;
            }

            if (!ExtractionItemLocationService.TryGetGrid(profile, null, entry.Container, out var sourceGrid)
                || !ExtractionItemLocationService.TryGetGrid(profile, null, targetContainer, out var targetGrid)
                || !sourceGrid.TryGetPlacement(itemInstanceId, out var sourcePlacement)
                || !targetGrid.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated)
                || !targetGrid.TryPlace(item, definition, x, y, rotated))
            {
                result = ExtractionItemLifecycleResult.NoSpace;
                return false;
            }

            if (!sourceGrid.TryRemove(itemInstanceId))
            {
                targetGrid.TryRemove(itemInstanceId);
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (!profile.Ownership.TryMove(itemInstanceId, entry.Container, targetContainer))
            {
                targetGrid.TryRemove(itemInstanceId);
                sourceGrid.TryPlace(
                    item,
                    definition,
                    sourcePlacement.X,
                    sourcePlacement.Y,
                    sourcePlacement.Rotated);
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TryDestroyAtBase(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            bool confirmed,
            out ExtractionItemLifecycleResult result)
        {
            result = confirmed
                ? ExtractionItemLifecycleResult.InvalidRequest
                : ExtractionItemLifecycleResult.Cancelled;
            if (!confirmed) return false;
            if (!ExtractionFeatureSwitch.Enabled
                || !TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                return false;
            }

            if (!ExtractionItemActionPolicyService.CanDrop(definition, item))
            {
                result = ExtractionItemLifecycleResult.PolicyDenied;
                return false;
            }
            if (!IsBaseLocation(entry))
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            if (!TryDetach(profile, null, item, definition, entry, out var restore))
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }
            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    entry.Container,
                    ExtractionInventoryContainerType.Destroyed,
                    "base-destroy",
                    null))
            {
                restore();
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TrySubmitToTask(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            string taskId,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || string.IsNullOrEmpty(taskId)
                || !TryGetItemAndLocation(
                    profile,
                    itemCatalog,
                    itemInstanceId,
                    out var item,
                    out var definition,
                    out var entry))
            {
                return false;
            }

            if (!ExtractionItemActionPolicyService.CanSubmitToTask(definition)) return false;
            if (!TryDetach(profile, raidInventory, item, definition, entry, out var restore))
            {
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }
            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    entry.Container,
                    ExtractionInventoryContainerType.Consumed,
                    "task",
                    taskId))
            {
                restore();
                result = ExtractionItemLifecycleResult.LocationConflict;
                return false;
            }

            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        public static bool TryApplyDeathPolicy(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string corpseId,
            out ExtractionItemLifecycleResult result)
        {
            result = ExtractionItemLifecycleResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile == null
                || raidInventory == null
                || itemCatalog == null
                || string.IsNullOrEmpty(corpseId))
            {
                return false;
            }

            profile.EnsureInitialized();
            raidInventory.EnsureInitialized();
            var plans = new List<DeathPlan>();
            var stagedHolding = CloneGrid(profile.RecoveryHolding);
            foreach (var entry in profile.Ownership.Entries)
            {
                if (entry == null || !IsRaidCarriedLocation(entry)) continue;
                if (!profile.Items.TryGet(entry.ItemInstanceId, out var item)
                    || !itemCatalog.TryGetItemDefinition(item.DefinitionId, out var definition))
                {
                    result = ExtractionItemLifecycleResult.ItemNotFound;
                    return false;
                }

                var policy = ExtractionItemActionPolicyService.GetPolicy(definition);
                bool destroy = policy != null
                               && (policy.DestroyOnSettlement
                                   || policy.RaidBound
                                   || item.HasFlag(ExtractionItemInstanceFlags.DestroyOnSettlement)
                                   || item.HasFlag(ExtractionItemInstanceFlags.RaidBound));
                bool protectedFromDeath = entry.Container == ExtractionInventoryContainerType.InSecureContainer
                                          || !ExtractionItemActionPolicyService.DropsOnDeath(definition, item);
                var target = destroy
                    ? ExtractionInventoryContainerType.Destroyed
                    : protectedFromDeath
                        ? ExtractionInventoryContainerType.Holding
                        : ExtractionInventoryContainerType.Corpse;

                if (target == ExtractionInventoryContainerType.Holding)
                {
                    if (!stagedHolding.TryFindFreeSlotWithRotation(
                            definition.Width,
                            definition.Height,
                            definition.CanRotate,
                            out int x,
                            out int y,
                            out bool rotated)
                        || !stagedHolding.TryPlace(item, definition, x, y, rotated))
                    {
                        result = ExtractionItemLifecycleResult.NoSpace;
                        return false;
                    }
                }

                plans.Add(new DeathPlan(entry, item, definition, target));
            }

            foreach (var plan in plans)
            {
                if (!TryDetach(profile, raidInventory, plan.Item, plan.Definition, plan.Entry, out _))
                {
                    result = ExtractionItemLifecycleResult.LocationConflict;
                    return false;
                }

                string subtype = plan.Target == ExtractionInventoryContainerType.Corpse
                    ? "corpse"
                    : plan.Target == ExtractionInventoryContainerType.Destroyed
                        ? "death-destroy"
                        : null;
                string locationId = plan.Target == ExtractionInventoryContainerType.Corpse ? corpseId : null;
                if (!profile.Ownership.TryMove(
                        plan.Item.InstanceId,
                        plan.Entry.Container,
                        plan.Target,
                        subtype,
                        locationId))
                {
                    result = ExtractionItemLifecycleResult.LocationConflict;
                    return false;
                }
            }

            profile.RecoveryHolding = stagedHolding;
            result = ExtractionItemLifecycleResult.Succeeded;
            return true;
        }

        private static bool TryGetItemAndLocation(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            string itemInstanceId,
            out ExtractionItemInstance item,
            out ExtractionItemDefinition definition,
            out ExtractionOwnershipEntry entry)
        {
            item = null;
            definition = null;
            entry = null;
            if (profile == null || itemCatalog == null || string.IsNullOrEmpty(itemInstanceId)) return false;
            profile.EnsureInitialized();
            if (!profile.Items.TryGet(itemInstanceId, out item)
                || !itemCatalog.TryGetItemDefinition(item.DefinitionId, out definition))
            {
                return false;
            }

            foreach (var candidate in profile.Ownership.Entries)
            {
                if (candidate?.ItemInstanceId != itemInstanceId) continue;
                if (entry != null) return false;
                entry = candidate;
            }

            return entry != null;
        }

        private static bool TryDetach(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            ExtractionItemInstance item,
            ExtractionItemDefinition definition,
            ExtractionOwnershipEntry entry,
            out Action restore)
        {
            restore = () => { };
            if (entry.Container == ExtractionInventoryContainerType.EquipmentSlot)
            {
                if (!ExtractionItemLocationService.TryGetEquipment(
                        profile,
                        raidInventory,
                        entry.LocationSubtype,
                        out var equipment)
                    || string.IsNullOrEmpty(entry.LocationId)
                    || !equipment.TryClear(entry.LocationId, item.InstanceId))
                {
                    return false;
                }

                restore = () => equipment.TrySet(entry.LocationId, item.InstanceId);
                return true;
            }

            if (ExtractionItemLocationService.TryGetGrid(
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
                return true;
            }

            return entry.Container == ExtractionInventoryContainerType.WorldPickup
                   || entry.Container == ExtractionInventoryContainerType.InRaid;
        }

        private static bool IsBaseLocation(ExtractionOwnershipEntry entry)
        {
            return entry.Container == ExtractionInventoryContainerType.Stash
                   || entry.Container == ExtractionInventoryContainerType.Loadout
                   || entry.Container == ExtractionInventoryContainerType.SecureContainer
                   || entry.Container == ExtractionInventoryContainerType.Holding
                   || (entry.Container == ExtractionInventoryContainerType.EquipmentSlot
                       && entry.LocationSubtype == ExtractionItemLocationService.BaseEquipmentLocationSubtype);
        }

        private static bool IsBaseGridLocation(ExtractionInventoryContainerType container)
        {
            return container == ExtractionInventoryContainerType.Stash
                   || container == ExtractionInventoryContainerType.Loadout
                   || container == ExtractionInventoryContainerType.SecureContainer;
        }

        private static bool IsRaidCarriedLocation(ExtractionOwnershipEntry entry)
        {
            return entry.Container == ExtractionInventoryContainerType.RaidBackpack
                   || entry.Container == ExtractionInventoryContainerType.InRaid
                   || entry.Container == ExtractionInventoryContainerType.InSecureContainer
                   || (entry.Container == ExtractionInventoryContainerType.EquipmentSlot
                       && entry.LocationSubtype == ExtractionItemLocationService.RaidEquipmentLocationSubtype);
        }

        private static ExtractionItemGrid CloneGrid(ExtractionItemGrid source)
        {
            var clone = new ExtractionItemGrid(source?.Width ?? 1, source?.Height ?? 1);
            if (source?.Placements == null) return clone;
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

        private sealed class DeathPlan
        {
            internal DeathPlan(
                ExtractionOwnershipEntry entry,
                ExtractionItemInstance item,
                ExtractionItemDefinition definition,
                ExtractionInventoryContainerType target)
            {
                Entry = entry;
                Item = item;
                Definition = definition;
                Target = target;
            }

            internal ExtractionOwnershipEntry Entry { get; }
            internal ExtractionItemInstance Item { get; }
            internal ExtractionItemDefinition Definition { get; }
            internal ExtractionInventoryContainerType Target { get; }
        }
    }
}
