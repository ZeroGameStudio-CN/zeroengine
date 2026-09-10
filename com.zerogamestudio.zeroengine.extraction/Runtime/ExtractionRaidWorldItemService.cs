using System;

namespace POB.Extraction
{
    /// <summary>World items belong to one raid, not to the persistent character inventory.</summary>
    public static class ExtractionRaidWorldItemService
    {
        public const string ExpiredLocationSubtype = "expired-raid-world-pickup";

        public static bool IsCurrentRaidItem(ExtractionProfileSaveData profile, string itemId)
        {
            var raid = profile?.ActiveRaid;
            return raid != null
                   && !string.IsNullOrEmpty(raid.RaidId)
                   && string.Equals(profile.activeRaidId, raid.RaidId, StringComparison.Ordinal)
                   && raid.Content?.WorldPickupItemInstanceIds != null
                   && raid.Content.WorldPickupItemInstanceIds.Contains(itemId);
        }

        // Idempotent repair also handles v2 profiles left with world ownership after settlement.
        // Retain the item and original location as a tombstone; never touch corpse/recovery,
        // stash, secure or carried items. Current-raid drops remain resumable.
        public static int ExpireWorldItems(ExtractionProfileSaveData profile, bool preserveCurrentRaid)
        {
            if (profile?.Ownership?.Entries == null) return 0;
            int expired = 0;
            foreach (var entry in profile.Ownership.Entries)
            {
                if (entry == null || entry.Container != ExtractionInventoryContainerType.WorldPickup
                    || (preserveCurrentRaid && IsCurrentRaidItem(profile, entry.ItemInstanceId))) continue;

                entry.Container = ExtractionInventoryContainerType.Destroyed;
                entry.LocationSubtype = ExpiredLocationSubtype;
                expired++;
            }
            if (!preserveCurrentRaid) profile.ActiveRaid?.Content?.WorldPickupItemInstanceIds?.Clear();
            return expired;
        }
    }
}
