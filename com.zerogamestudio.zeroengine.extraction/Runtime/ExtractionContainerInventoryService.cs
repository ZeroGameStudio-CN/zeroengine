using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    // Container entries own their persisted geometry and reveal state. The item registry owns
    // mutable item state once first operated on. All mutations below require a staged profile.
    public static class ExtractionContainerInventoryService
    {
        public const string LocationSubtype = "raid-container";

        public static bool TryGetRevealedItem(ExtractionProfileSaveData profile, IExtractionItemCatalog catalog,
            string itemId, out ExtractionItemInstance item, out ExtractionRaidContainerManifest container,
            out ExtractionContainerLootEntry entry)
        {
            item = null;
            if (!TryFindRevealedEntry(profile, itemId, out container, out entry)
                || !container.Active || !container.Opened || catalog == null
                || !catalog.TryGetItemDefinition(entry.DefinitionId, out var definition)) return false;
            if (profile.Items.TryGet(itemId, out item))
            {
                var location = FindOwnership(profile, itemId);
                return location != null && location.Container == ExtractionInventoryContainerType.RaidContainer
                    && location.LocationId == container.ContainerId && item.DefinitionId == entry.DefinitionId;
            }
            if (profile.Ownership.TryGetContainer(itemId, out _) || entry.Quantity <= 0) return false;
            item = new ExtractionItemInstance(itemId, entry.DefinitionId, entry.Quantity, "container", container.ContainerId);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(definition, item);
            return true;
        }

        public static bool TryMaterialize(ExtractionProfileSaveData profile, IExtractionItemCatalog catalog, string itemId)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (!TryGetRevealedItem(profile, catalog, itemId, out var item, out var container, out _)) return false;
            if (profile.Items.TryGet(itemId, out _)) return true;
            if (!profile.Items.Register(item)) return false;
            if (profile.Ownership.Register(itemId, ExtractionInventoryContainerType.RaidContainer,
                    LocationSubtype, container.ContainerId)) return true;
            profile.Items.TryRemove(itemId);
            return false;
        }

        public static bool TryFindRevealedEntry(ExtractionProfileSaveData profile, string itemId,
            out ExtractionRaidContainerManifest container, out ExtractionContainerLootEntry entry)
        {
            container = null; entry = null;
            var containers = profile?.ActiveRaid?.Content?.LootManifest?.Containers;
            if (containers == null || string.IsNullOrEmpty(itemId)
                || string.IsNullOrEmpty(profile.ActiveRaid.RaidId) || profile.activeRaidId != profile.ActiveRaid.RaidId) return false;
            foreach (var candidate in containers)
            {
                if (candidate?.Entries == null) continue;
                foreach (var value in candidate.Entries)
                    if (value?.ItemInstanceId == itemId && value.State == ExtractionContainerLootEntryState.Revealed)
                    {
                        if (entry != null) { container = null; entry = null; return false; }
                        container = candidate; entry = value;
                    }
            }
            return entry != null;
        }

        public static bool TryBuildGrid(ExtractionRaidContainerManifest container, bool overflow, out ExtractionItemGrid grid)
        {
            grid = null;
            if (container?.Entries == null || !container.Opened || container.LayoutVersion != 1
                || container.Columns <= 0 || container.Rows <= 0) return false;
            int columns = container.Columns, rows = overflow ? 0 : container.Rows;
            if (overflow)
                foreach (var entry in container.Entries)
                    if (entry?.Layout != null && entry.LayoutOverflow)
                    {
                        columns = Math.Max(columns, entry.Layout.X + entry.Layout.Width);
                        rows = Math.Max(rows, entry.Layout.Y + entry.Layout.Height);
                    }
            if (rows <= 0) return false;
            grid = new ExtractionItemGrid(columns, rows);
            foreach (var entry in container.Entries)
            {
                if (entry == null || entry.State == ExtractionContainerLootEntryState.Transferred
                    || entry.LayoutOverflow != overflow) continue;
                var placement = entry.Layout;
                if (placement == null || string.IsNullOrEmpty(entry.ItemInstanceId)
                    || grid.TryGetPlacement(entry.ItemInstanceId, out _)
                    || !grid.CanPlace(placement.X, placement.Y, placement.Width, placement.Height)) return false;
                grid.Placements.Add(new ExtractionItemPlacement(entry.ItemInstanceId, placement.X, placement.Y,
                    placement.Width, placement.Height, placement.Rotated));
            }
            return true;
        }

        public static bool TryDetach(ExtractionProfileSaveData profile, string itemId, out Action restore)
        {
            restore = () => { };
            if (!TryFindRevealedEntry(profile, itemId, out var container, out var entry)
                || !container.Active || !container.Opened) return false;
            var location = FindOwnership(profile, itemId);
            if (location == null || location.Container != ExtractionInventoryContainerType.RaidContainer
                || location.LocationId != container.ContainerId) return false;
            string receipt = entry.TransferReceiptId;
            entry.State = ExtractionContainerLootEntryState.Transferred;
            restore = () => { entry.State = ExtractionContainerLootEntryState.Revealed; entry.TransferReceiptId = receipt; };
            return true;
        }

        public static bool TryMove(ExtractionProfileSaveData profile, ExtractionRaidInventoryState raid,
            IExtractionItemCatalog catalog, string itemId, ExtractionInventoryContainerType target,
            string targetContainerId, int x, int y, bool rotated, bool autoPlace, string receipt,
            out ExtractionContainerTransferResult result)
        {
            result = ExtractionContainerTransferResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled || profile?.ActiveRaid == null || raid == null || catalog == null
                || string.IsNullOrEmpty(receipt) || string.IsNullOrEmpty(itemId)) return false;
            if (profile.ItemActionReceiptIds.Contains(receipt))
            { result = ExtractionContainerTransferResult.AlreadyTransferred; return true; }

            ExtractionRaidContainerManifest sourceContainer = null;
            ExtractionContainerLootEntry sourceEntry = null;
            ExtractionItemInstance item;
            var location = FindOwnership(profile, itemId);
            if (location == null || location.Container == ExtractionInventoryContainerType.RaidContainer)
            {
                if (!TryGetRevealedItem(profile, catalog, itemId, out item, out sourceContainer, out sourceEntry))
                { result = ExtractionContainerTransferResult.NotRevealed; return false; }
            }
            else if (!profile.Items.TryGet(itemId, out item)) return false;
            if (item.Quantity <= 0 || !catalog.TryGetItemDefinition(item.DefinitionId, out var definition))
            { result = ExtractionContainerTransferResult.DefinitionNotFound; return false; }
            if (target != ExtractionInventoryContainerType.RaidBackpack
                && target != ExtractionInventoryContainerType.InSecureContainer
                && target != ExtractionInventoryContainerType.RaidContainer) return false;
            if (target == ExtractionInventoryContainerType.InSecureContainer
                && !ExtractionItemActionPolicyService.CanPlaceInSecure(definition))
            { result = ExtractionContainerTransferResult.PolicyDenied; return false; }

            ExtractionItemGrid sourceGrid;
            bool sourceOverflow = sourceEntry?.LayoutOverflow == true;
            if (sourceContainer != null)
            {
                if (!TryBuildGrid(sourceContainer, sourceOverflow, out sourceGrid)) return false;
            }
            else if ((location.Container != ExtractionInventoryContainerType.RaidBackpack
                      && location.Container != ExtractionInventoryContainerType.InSecureContainer)
                     || !ExtractionItemLocationService.TryGetGrid(profile, raid, location.Container, out sourceGrid)) return false;
            if (!sourceGrid.TryGetPlacement(itemId, out var original)) return false;

            ExtractionRaidContainerManifest destination = null;
            ExtractionItemGrid targetGrid;
            if (target == ExtractionInventoryContainerType.RaidContainer)
            {
                if (!profile.ActiveRaid.Content.LootManifest.TryGetContainer(targetContainerId, out destination)
                    || !destination.Active || !destination.Opened) return false;
                if (destination == sourceContainer && !sourceOverflow) targetGrid = sourceGrid;
                else if (!TryBuildGrid(destination, false, out targetGrid)) return false;
            }
            else if (!ExtractionItemLocationService.TryGetGrid(profile, raid, target, out targetGrid)) return false;
            var probe = CloneGrid(targetGrid);
            if (ReferenceEquals(sourceGrid, targetGrid)) probe.TryRemove(itemId);
            if (autoPlace && !probe.TryFindFreeSlotWithRotation(definition.Width, definition.Height, definition.CanRotate,
                    out x, out y, out rotated))
            { result = ExtractionContainerTransferResult.NoSpace; return false; }
            if (!probe.TryPlace(item, definition, x, y, rotated))
            { result = ExtractionContainerTransferResult.NoSpace; return false; }
            if (sourceContainer != null && !TryMaterialize(profile, catalog, itemId)) return false;
            location = FindOwnership(profile, itemId);
            var sourceType = location.Container;
            if (!sourceGrid.TryRemove(itemId) || !targetGrid.TryPlace(item, definition, x, y, rotated)) return false;
            if (!profile.Ownership.TryMove(itemId, sourceType, target,
                    target == ExtractionInventoryContainerType.RaidContainer ? LocationSubtype : null,
                    target == ExtractionInventoryContainerType.RaidContainer ? targetContainerId : null)) return false;
            if (sourceContainer != null && !ApplyGrid(profile, catalog, sourceContainer, sourceGrid, sourceOverflow, receipt)) return false;
            if (destination != null && !ReferenceEquals(sourceGrid, targetGrid)
                && !ApplyGrid(profile, catalog, destination, targetGrid, false, receipt)) return false;
            profile.ItemActionReceiptIds.Add(receipt);
            result = ExtractionContainerTransferResult.Succeeded;
            return true;
        }

        public static bool TryEquip(ExtractionProfileSaveData profile, ExtractionRaidInventoryState raid,
            IExtractionItemCatalog catalog, string itemId, string slotId, ExtractionEquipmentSlotType slotType,
            string receipt, out string displaced, out ExtractionEquipmentTransactionResult result)
        {
            displaced = null; result = ExtractionEquipmentTransactionResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled || string.IsNullOrEmpty(receipt)) return false;
            if (raid?.Equipment?.AppliedReceiptIds?.Contains(receipt) == true)
            { result = ExtractionEquipmentTransactionResult.AlreadyApplied; return true; }
            if (!TryGetRevealedItem(profile, catalog, itemId, out _, out var container, out var entry)
                || !TryBuildGrid(container, entry.LayoutOverflow, out var grid)
                || !TryMaterialize(profile, catalog, itemId)) return false;
            if (!ExtractionEquipmentTransactionService.TryEquipWithSourceGrid(profile, raid, catalog, itemId,
                    ExtractionInventoryContainerType.RaidContainer, ExtractionItemLocationService.RaidEquipmentLocationSubtype,
                    slotId, slotType, receipt, out displaced, out result, grid, container.ContainerId)) return false;
            return ApplyGrid(profile, catalog, container, grid, entry.LayoutOverflow, receipt);
        }

        private static bool ApplyGrid(ExtractionProfileSaveData profile, IExtractionItemCatalog catalog,
            ExtractionRaidContainerManifest container, ExtractionItemGrid grid, bool overflow, string receipt)
        {
            foreach (var entry in container.Entries)
                if (entry != null && entry.State != ExtractionContainerLootEntryState.Transferred
                    && entry.LayoutOverflow == overflow && !grid.TryGetPlacement(entry.ItemInstanceId, out _))
                { entry.State = ExtractionContainerLootEntryState.Transferred; entry.TransferReceiptId = receipt; }
            foreach (var placement in grid.Placements)
            {
                ExtractionContainerLootEntry entry = null;
                foreach (var candidate in container.Entries)
                    if (candidate?.ItemInstanceId == placement.ItemInstanceId) { entry = candidate; break; }
                if (entry == null)
                {
                    if (!profile.Items.TryGet(placement.ItemInstanceId, out var item)
                        || !catalog.TryGetItemDefinition(item.DefinitionId, out var definition)) return false;
                    entry = new ExtractionContainerLootEntry(container.ContainerId + ":stored:" + item.InstanceId,
                        item.InstanceId, item.DefinitionId, item.Quantity, definition.Rarity, false)
                    { State = ExtractionContainerLootEntryState.Revealed, RevealOrder = -1, RevealReceiptId = receipt };
                    container.Entries.Add(entry);
                }
                if (entry.State == ExtractionContainerLootEntryState.Transferred)
                { entry.State = ExtractionContainerLootEntryState.Revealed; entry.TransferReceiptId = null; }
                if (profile.Items.TryGet(entry.ItemInstanceId, out var stored)) entry.Quantity = stored.Quantity;
                entry.Layout = new ExtractionItemPlacement(entry.EntryId, placement.X, placement.Y,
                    placement.Width, placement.Height, placement.Rotated);
                entry.LayoutOverflow = overflow;
            }
            return true;
        }

        private static ExtractionOwnershipEntry FindOwnership(ExtractionProfileSaveData profile, string itemId)
        {
            ExtractionOwnershipEntry found = null;
            if (profile?.Ownership?.Entries == null) return null;
            foreach (var entry in profile.Ownership.Entries)
                if (entry?.ItemInstanceId == itemId) { if (found != null) return null; found = entry; }
            return found;
        }

        private static ExtractionItemGrid CloneGrid(ExtractionItemGrid source)
        {
            var clone = new ExtractionItemGrid(source.Width, source.Height);
            foreach (var placement in source.Placements)
                clone.Placements.Add(new ExtractionItemPlacement(placement.ItemInstanceId, placement.X,
                    placement.Y, placement.Width, placement.Height, placement.Rotated));
            return clone;
        }
    }
}
