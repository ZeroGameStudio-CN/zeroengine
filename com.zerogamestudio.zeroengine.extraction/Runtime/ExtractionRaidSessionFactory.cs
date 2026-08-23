using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionRaidSessionFactory
    {
        public static bool TryCreate(
            ExtractionProfileSaveData profile,
            ExtractionMapDefinition map,
            ExtractionRaidStartRequest request,
            out ExtractionRaidSession session)
        {
            return TryCreateInternal(profile, map, request, null, out session);
        }

        public static bool TryCreate(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            ExtractionMapDefinition map,
            ExtractionRaidStartRequest request,
            bool rareLootDisabled,
            out ExtractionRaidSession session,
            out ExtractionRaidLootManifestFailure manifestFailure)
        {
            session = null;
            manifestFailure = ExtractionRaidLootManifestFailure.None;
            if (profile == null || config == null || map == null || request == null) return false;

            if (!ExtractionRaidLootManifestGenerator.TryGenerate(
                    config,
                    map,
                    request.Seed,
                    rareLootDisabled,
                    out var manifest,
                    out manifestFailure))
            {
                return false;
            }

            ExtractionRaidRuleSnapshot ruleSnapshot = null;
            if (!string.IsNullOrEmpty(map.RaidRuleProfileId)
                && !ExtractionRaidMechanicsService.TryCreateRuleSnapshot(
                    config,
                    map,
                    map.ThreatLevel,
                    out ruleSnapshot))
            {
                return false;
            }

            if (!TryCreateInternal(profile, map, request, manifest, out session)) return false;
            if (ruleSnapshot == null || session.TrySetRuleSnapshot(ruleSnapshot)) return true;

            profile.ActiveRaid = null;
            profile.activeRaidId = null;
            session = null;
            return false;
        }

        private static bool TryCreateInternal(
            ExtractionProfileSaveData profile,
            ExtractionMapDefinition map,
            ExtractionRaidStartRequest request,
            ExtractionRaidLootManifest manifest,
            out ExtractionRaidSession session)
        {
            session = null;
            if (profile == null || map == null || request == null) return false;
            if (!map.IsValid || !request.IsValid) return false;
            if (!string.IsNullOrEmpty(profile.activeRaidId) || profile.ActiveRaid != null) return false;

            var movedLoadout = new List<string>();
            var movedSecure = new List<string>();
            var movedEquipment = new List<ExtractionEquipmentSlotState>();

            var loadoutTarget = request.UseUnifiedItemLocations
                ? ExtractionInventoryContainerType.RaidBackpack
                : ExtractionInventoryContainerType.InRaid;

            foreach (var itemId in request.LoadoutItemInstanceIds)
            {
                if (!profile.Ownership.TryMove(
                        itemId,
                        ExtractionInventoryContainerType.Loadout,
                        loadoutTarget))
                {
                    Rollback(profile, movedLoadout, movedSecure, movedEquipment, loadoutTarget);
                    return false;
                }

                movedLoadout.Add(itemId);
            }

            foreach (var itemId in request.SecureItemInstanceIds)
            {
                if (!profile.Ownership.TryMove(
                        itemId,
                        ExtractionInventoryContainerType.SecureContainer,
                        ExtractionInventoryContainerType.InSecureContainer))
                {
                    Rollback(profile, movedLoadout, movedSecure, movedEquipment, loadoutTarget);
                    return false;
                }

                movedSecure.Add(itemId);
            }

            if (request.EquipmentSlots != null)
            {
                foreach (var slot in request.EquipmentSlots)
                {
                    if (slot == null
                        || string.IsNullOrEmpty(slot.SlotId)
                        || string.IsNullOrEmpty(slot.ItemInstanceId)
                        || !profile.Ownership.TryMove(
                            slot.ItemInstanceId,
                            ExtractionInventoryContainerType.EquipmentSlot,
                            ExtractionInventoryContainerType.EquipmentSlot,
                            ExtractionItemLocationService.RaidEquipmentLocationSubtype,
                            slot.SlotId))
                    {
                        Rollback(profile, movedLoadout, movedSecure, movedEquipment, loadoutTarget);
                        return false;
                    }

                    movedEquipment.Add(slot);
                }
            }

            session = new ExtractionRaidSession(map, request);
            session.Content.LootManifest = manifest;
            profile.ActiveRaid = session;
            profile.activeRaidId = session.RaidId;
            return true;
        }

        private static void Rollback(
            ExtractionProfileSaveData profile,
            List<string> movedLoadout,
            List<string> movedSecure,
            List<ExtractionEquipmentSlotState> movedEquipment,
            ExtractionInventoryContainerType loadoutTarget)
        {
            foreach (var slot in movedEquipment)
            {
                profile.Ownership.TryMove(
                    slot.ItemInstanceId,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    ExtractionInventoryContainerType.EquipmentSlot,
                    ExtractionItemLocationService.BaseEquipmentLocationSubtype,
                    slot.SlotId);
            }

            foreach (var itemId in movedSecure)
            {
                profile.Ownership.TryMove(
                    itemId,
                    ExtractionInventoryContainerType.InSecureContainer,
                    ExtractionInventoryContainerType.SecureContainer);
            }

            foreach (var itemId in movedLoadout)
            {
                profile.Ownership.TryMove(
                    itemId,
                    loadoutTarget,
                    ExtractionInventoryContainerType.Loadout);
            }
        }
    }
}
