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
            session = null;
            if (profile == null || map == null || request == null) return false;
            if (!map.IsValid || !request.IsValid) return false;
            if (!string.IsNullOrEmpty(profile.activeRaidId) || profile.ActiveRaid != null) return false;

            var movedLoadout = new List<string>();
            var movedSecure = new List<string>();

            foreach (var itemId in request.LoadoutItemInstanceIds)
            {
                if (!profile.Ownership.TryMove(
                        itemId,
                        ExtractionInventoryContainerType.Loadout,
                        ExtractionInventoryContainerType.InRaid))
                {
                    Rollback(profile, movedLoadout, movedSecure);
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
                    Rollback(profile, movedLoadout, movedSecure);
                    return false;
                }

                movedSecure.Add(itemId);
            }

            session = new ExtractionRaidSession(map, request);
            profile.ActiveRaid = session;
            profile.activeRaidId = session.RaidId;
            return true;
        }

        private static void Rollback(
            ExtractionProfileSaveData profile,
            List<string> movedLoadout,
            List<string> movedSecure)
        {
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
                    ExtractionInventoryContainerType.InRaid,
                    ExtractionInventoryContainerType.Loadout);
            }
        }
    }
}
