namespace POB.Extraction
{
    public static class ExtractionProfileInventoryService
    {
        public static bool TryMoveItem(
            ExtractionProfileSaveData profile,
            string itemInstanceId,
            ExtractionInventoryContainerType source,
            ExtractionInventoryContainerType target,
            ExtractionItemDefinition definition,
            int x,
            int y,
            bool rotated)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || string.IsNullOrEmpty(itemInstanceId) || definition == null) return false;
            if (source == target) return false;

            profile.EnsureInitialized();
            if (!profile.Items.TryGet(itemInstanceId, out var item)) return false;
            if (!profile.Ownership.TryGetContainer(itemInstanceId, out var current) || current != source)
                return false;
            if (!TryGetGrid(profile, source, out var sourceGrid)) return false;
            if (!TryGetGrid(profile, target, out var targetGrid)) return false;
            if (!sourceGrid.TryGetPlacement(itemInstanceId, out var sourcePlacement)) return false;
            if (!targetGrid.TryPlace(item, definition, x, y, rotated)) return false;

            if (!sourceGrid.TryRemove(itemInstanceId))
            {
                targetGrid.TryRemove(itemInstanceId);
                return false;
            }

            if (profile.Ownership.TryMove(itemInstanceId, source, target)) return true;

            targetGrid.TryRemove(itemInstanceId);
            sourceGrid.TryPlace(item, definition, sourcePlacement.X, sourcePlacement.Y, sourcePlacement.Rotated);
            return false;
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
