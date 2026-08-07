using System;

namespace POB.Extraction
{
    public enum ExtractionContainerTransferResult
    {
        Succeeded = 0,
        AlreadyTransferred = 1,
        InvalidRequest = 2,
        EntryNotFound = 3,
        NotRevealed = 4,
        DefinitionNotFound = 5,
        PolicyDenied = 6,
        NoSpace = 7,
        LocationConflict = 8,
        CommitFailed = 9
    }

    public static class ExtractionContainerTransferService
    {
        public static bool TryTransfer(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            IExtractionItemCatalog itemCatalog,
            string containerId,
            string entryId,
            ExtractionInventoryContainerType targetContainer,
            string transferReceiptId,
            out string itemInstanceId,
            out ExtractionContainerTransferResult result)
        {
            itemInstanceId = null;
            result = ExtractionContainerTransferResult.InvalidRequest;
            if (!ExtractionFeatureSwitch.Enabled
                || profile?.ActiveRaid?.Content?.LootManifest == null
                || raidInventory == null
                || itemCatalog == null
                || string.IsNullOrEmpty(containerId)
                || string.IsNullOrEmpty(entryId)
                || string.IsNullOrEmpty(transferReceiptId)
                || (targetContainer != ExtractionInventoryContainerType.RaidBackpack
                    && targetContainer != ExtractionInventoryContainerType.InSecureContainer))
            {
                return false;
            }

            profile.EnsureInitialized();
            raidInventory.EnsureInitialized();
            if (!TryGetEntry(profile.ActiveRaid, containerId, entryId, out var entry))
            {
                result = ExtractionContainerTransferResult.EntryNotFound;
                return false;
            }

            itemInstanceId = entry.ItemInstanceId;
            if (entry.State == ExtractionContainerLootEntryState.Transferred)
            {
                if (entry.TransferReceiptId == transferReceiptId
                    && profile.Items.TryGet(itemInstanceId, out _)
                    && profile.Ownership.TryGetContainer(itemInstanceId, out var existingContainer)
                    && existingContainer == targetContainer)
                {
                    result = ExtractionContainerTransferResult.AlreadyTransferred;
                    return true;
                }

                result = ExtractionContainerTransferResult.LocationConflict;
                return false;
            }

            if (entry.State != ExtractionContainerLootEntryState.Revealed)
            {
                result = ExtractionContainerTransferResult.NotRevealed;
                return false;
            }
            if (string.IsNullOrEmpty(itemInstanceId)
                || string.IsNullOrEmpty(entry.DefinitionId)
                || entry.Quantity <= 0
                || !itemCatalog.TryGetItemDefinition(entry.DefinitionId, out var definition))
            {
                result = ExtractionContainerTransferResult.DefinitionNotFound;
                return false;
            }
            if (targetContainer == ExtractionInventoryContainerType.InSecureContainer
                && !ExtractionItemActionPolicyService.CanPlaceInSecure(definition))
            {
                result = ExtractionContainerTransferResult.PolicyDenied;
                return false;
            }
            if (profile.Items.TryGet(itemInstanceId, out _)
                || profile.Ownership.TryGetContainer(itemInstanceId, out _))
            {
                result = ExtractionContainerTransferResult.LocationConflict;
                return false;
            }
            if (!ExtractionItemLocationService.TryGetGrid(
                    profile,
                    raidInventory,
                    targetContainer,
                    out var targetGrid)
                || !targetGrid.TryFindFreeSlotWithRotation(
                    definition.Width,
                    definition.Height,
                    definition.CanRotate,
                    out int x,
                    out int y,
                    out bool rotated))
            {
                result = ExtractionContainerTransferResult.NoSpace;
                return false;
            }

            var item = new ExtractionItemInstance(
                itemInstanceId,
                entry.DefinitionId,
                entry.Quantity,
                "container",
                containerId);
            ExtractionItemActionPolicyService.ApplyDefinitionPolicyToInstance(definition, item);
            if (!profile.Items.Register(item))
            {
                result = ExtractionContainerTransferResult.LocationConflict;
                return false;
            }
            if (!targetGrid.TryPlace(item, definition, x, y, rotated))
            {
                profile.Items.TryRemove(itemInstanceId);
                result = ExtractionContainerTransferResult.NoSpace;
                return false;
            }
            if (!profile.Ownership.Register(itemInstanceId, targetContainer))
            {
                targetGrid.TryRemove(itemInstanceId);
                profile.Items.TryRemove(itemInstanceId);
                result = ExtractionContainerTransferResult.LocationConflict;
                return false;
            }
            if (!ExtractionContainerSearchService.TryMarkTransferred(
                    profile.ActiveRaid,
                    containerId,
                    entryId,
                    transferReceiptId))
            {
                profile.Ownership.TryRemove(itemInstanceId);
                targetGrid.TryRemove(itemInstanceId);
                profile.Items.TryRemove(itemInstanceId);
                result = ExtractionContainerTransferResult.LocationConflict;
                return false;
            }

            result = ExtractionContainerTransferResult.Succeeded;
            return true;
        }

        private static bool TryGetEntry(
            ExtractionRaidSession raid,
            string containerId,
            string entryId,
            out ExtractionContainerLootEntry entry)
        {
            entry = null;
            var manifest = raid?.Content?.LootManifest;
            if (manifest == null || !manifest.TryGetContainer(containerId, out var container)) return false;

            foreach (var candidate in container.Entries)
            {
                if (candidate?.EntryId != entryId) continue;
                if (entry != null) return false;
                entry = candidate;
            }

            return entry != null;
        }
    }
}
