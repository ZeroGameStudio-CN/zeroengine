namespace POB.Extraction
{
    public static class ExtractionLootPickupService
    {
        public static bool TryPickup(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState inventory,
            ExtractionLootPickup pickup,
            ExtractionPickupTarget target,
            int x,
            int y,
            bool rotated)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || inventory == null || pickup == null || !pickup.IsValid) return false;
            if (profile.ActiveRaid == null || string.IsNullOrEmpty(profile.activeRaidId)) return false;

            if (!TryResolveTarget(
                    inventory,
                    pickup,
                    target,
                    out var grid,
                    out var container))
            {
                return false;
            }

            profile.EnsureInitialized();

            if (!grid.TryPlace(pickup.Item, pickup.Definition, x, y, rotated)) return false;
            if (!profile.Ownership.Register(pickup.Item.InstanceId, container))
            {
                grid.TryRemove(pickup.Item.InstanceId);
                return false;
            }

            if (profile.Items.Register(pickup.Item)) return true;

            profile.Ownership.TryRemove(pickup.Item.InstanceId);
            grid.TryRemove(pickup.Item.InstanceId);
            return false;
        }

        private static bool TryResolveTarget(
            ExtractionRaidInventoryState inventory,
            ExtractionLootPickup pickup,
            ExtractionPickupTarget target,
            out ExtractionItemGrid grid,
            out ExtractionInventoryContainerType container)
        {
            if (target == ExtractionPickupTarget.RaidBackpack)
            {
                grid = inventory.RaidBackpack;
                container = ExtractionInventoryContainerType.RaidBackpack;
                return grid != null;
            }

            if (target == ExtractionPickupTarget.SecureContainer)
            {
                grid = inventory.SecureContainer;
                container = ExtractionInventoryContainerType.InSecureContainer;
                return pickup.CanEnterSecureContainer
                       && ExtractionItemActionPolicyService.CanPlaceInSecure(pickup.Definition)
                       && grid != null;
            }

            grid = null;
            container = default;
            return false;
        }
    }
}
