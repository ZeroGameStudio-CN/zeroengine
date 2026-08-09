using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionItemLocationService
    {
        public const string BaseEquipmentLocationSubtype = "base-equipment";
        public const string RaidEquipmentLocationSubtype = "raid-equipment";

        public static bool TryValidate(
            ExtractionProfileSaveData profile,
            out string issue)
        {
            issue = null;
            if (profile == null) return Fail("Profile is null.", out issue);
            profile.EnsureInitialized();

            var registryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in profile.Items.Entries)
            {
                if (item == null || string.IsNullOrEmpty(item.InstanceId))
                    return Fail("Item registry contains an invalid entry.", out issue);
                if (!registryIds.Add(item.InstanceId))
                    return Fail($"Item '{item.InstanceId}' is registered more than once.", out issue);
            }

            var ownership = new Dictionary<string, ExtractionOwnershipEntry>(StringComparer.Ordinal);
            foreach (var entry in profile.Ownership.Entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ItemInstanceId))
                    return Fail("Ownership ledger contains an invalid entry.", out issue);
                if (!registryIds.Contains(entry.ItemInstanceId))
                    return Fail($"Ownership references unknown item '{entry.ItemInstanceId}'.", out issue);
                if (!ownership.TryAdd(entry.ItemInstanceId, entry))
                    return Fail($"Item '{entry.ItemInstanceId}' has more than one ownership location.", out issue);
            }

            foreach (string itemId in registryIds)
            {
                if (!ownership.ContainsKey(itemId))
                    return Fail($"Item '{itemId}' has no ownership location.", out issue);
            }

            var physicalLocations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!CollectGrid(profile.Stash, "stash", physicalLocations, out issue)
                || !CollectGrid(profile.CarryGrid, "carry-grid", physicalLocations, out issue)
                || !CollectGrid(profile.SecureContainer, "secure", physicalLocations, out issue)
                || !CollectGrid(profile.RecoveryHolding, "holding", physicalLocations, out issue)
                || !CollectGrid(profile.ActiveRaidInventory?.RaidBackpack, "raid-backpack", physicalLocations, out issue)
                || !CollectGrid(profile.ActiveRaidInventory?.SecureContainer, "raid-secure", physicalLocations, out issue)
                || !CollectEquipment(profile.Equipment, BaseEquipmentLocationSubtype, physicalLocations, out issue)
                || !CollectEquipment(profile.ActiveRaidInventory?.Equipment, RaidEquipmentLocationSubtype, physicalLocations, out issue))
            {
                return false;
            }

            foreach (var pair in ownership)
            {
                var entry = pair.Value;
                if (entry.Container == ExtractionInventoryContainerType.EquipmentSlot)
                {
                    if (entry.LocationSubtype != BaseEquipmentLocationSubtype
                        && entry.LocationSubtype != RaidEquipmentLocationSubtype)
                    {
                        return Fail($"Equipped item '{pair.Key}' has no valid equipment scope.", out issue);
                    }

                    if (!physicalLocations.TryGetValue(pair.Key, out string actual)
                        || actual != entry.LocationSubtype + ":" + entry.LocationId)
                    {
                        return Fail($"Equipped item '{pair.Key}' does not match slot '{entry.LocationId}'.", out issue);
                    }

                    continue;
                }

                if (TryGetExpectedPhysicalLocation(entry.Container, out string expected))
                {
                    if (!physicalLocations.TryGetValue(pair.Key, out string actual) || actual != expected)
                        return Fail($"Item '{pair.Key}' does not match its '{entry.Container}' grid location.", out issue);
                    continue;
                }

                if (physicalLocations.ContainsKey(pair.Key))
                    return Fail($"Item '{pair.Key}' is both '{entry.Container}' and physically stored.", out issue);
            }

            foreach (var pair in physicalLocations)
            {
                if (!ownership.ContainsKey(pair.Key))
                    return Fail($"Physical item '{pair.Key}' is missing from ownership.", out issue);
            }

            return true;
        }

        public static int CalculateWeight(
            ExtractionProfileSaveData profile,
            IExtractionItemCatalog itemCatalog,
            params ExtractionInventoryContainerType[] containers)
        {
            if (profile == null || itemCatalog == null || containers == null || containers.Length == 0)
                return 0;

            var included = new HashSet<ExtractionInventoryContainerType>(containers);
            long total = 0;
            foreach (var entry in profile.Ownership.Entries)
            {
                if (entry == null || !included.Contains(entry.Container)) continue;
                if (!profile.Items.TryGet(entry.ItemInstanceId, out var item)) continue;
                if (!itemCatalog.TryGetItemDefinition(item.DefinitionId, out var definition)) continue;

                total += (long)Math.Max(0, definition.Weight) * Math.Max(0, item.Quantity);
                if (total >= int.MaxValue) return int.MaxValue;
            }

            return (int)total;
        }

        internal static bool TryGetGrid(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            ExtractionInventoryContainerType container,
            out ExtractionItemGrid grid)
        {
            grid = container switch
            {
                ExtractionInventoryContainerType.Stash => profile?.Stash,
                ExtractionInventoryContainerType.Loadout => profile?.CarryGrid,
                ExtractionInventoryContainerType.SecureContainer => profile?.SecureContainer,
                ExtractionInventoryContainerType.Holding => profile?.RecoveryHolding,
                ExtractionInventoryContainerType.RaidBackpack => raidInventory?.RaidBackpack,
                ExtractionInventoryContainerType.InSecureContainer => raidInventory?.SecureContainer,
                _ => null
            };
            return grid != null;
        }

        internal static bool TryGetEquipment(
            ExtractionProfileSaveData profile,
            ExtractionRaidInventoryState raidInventory,
            string locationSubtype,
            out ExtractionEquipmentState equipment)
        {
            equipment = locationSubtype switch
            {
                BaseEquipmentLocationSubtype => profile?.Equipment,
                RaidEquipmentLocationSubtype => raidInventory?.Equipment,
                _ => null
            };
            return equipment != null;
        }

        private static bool TryGetExpectedPhysicalLocation(
            ExtractionInventoryContainerType container,
            out string location)
        {
            location = container switch
            {
                ExtractionInventoryContainerType.Stash => "stash",
                ExtractionInventoryContainerType.Loadout => "carry-grid",
                ExtractionInventoryContainerType.SecureContainer => "secure",
                ExtractionInventoryContainerType.Holding => "holding",
                ExtractionInventoryContainerType.RaidBackpack => "raid-backpack",
                ExtractionInventoryContainerType.InSecureContainer => "raid-secure",
                _ => null
            };
            return location != null;
        }

        private static bool CollectGrid(
            ExtractionItemGrid grid,
            string location,
            Dictionary<string, string> physicalLocations,
            out string issue)
        {
            issue = null;
            if (grid?.Placements == null) return true;

            foreach (var placement in grid.Placements)
            {
                if (placement == null || string.IsNullOrEmpty(placement.ItemInstanceId))
                    return Fail($"Grid '{location}' contains an invalid placement.", out issue);
                if (!physicalLocations.TryAdd(placement.ItemInstanceId, location))
                    return Fail($"Item '{placement.ItemInstanceId}' appears in multiple physical locations.", out issue);
            }

            return true;
        }

        private static bool CollectEquipment(
            ExtractionEquipmentState equipment,
            string locationSubtype,
            Dictionary<string, string> physicalLocations,
            out string issue)
        {
            issue = null;
            if (equipment?.Slots == null) return true;

            var slotIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in equipment.Slots)
            {
                if (slot == null || string.IsNullOrEmpty(slot.SlotId) || string.IsNullOrEmpty(slot.ItemInstanceId))
                    return Fail($"Equipment '{locationSubtype}' contains an invalid slot.", out issue);
                if (!slotIds.Add(slot.SlotId))
                    return Fail($"Equipment slot '{slot.SlotId}' appears more than once.", out issue);
                if (!physicalLocations.TryAdd(slot.ItemInstanceId, locationSubtype + ":" + slot.SlotId))
                    return Fail($"Item '{slot.ItemInstanceId}' appears in multiple physical locations.", out issue);
            }

            return true;
        }

        private static bool Fail(string message, out string issue)
        {
            issue = message;
            return false;
        }
    }
}
