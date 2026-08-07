namespace POB.Extraction
{
    public static class ExtractionCorpseLootService
    {
        public static bool TryLootCorpseItem(
            ExtractionProfileSaveData profile,
            ExtractionCorpseLootLedger corpseLedger,
            string corpseId,
            string itemInstanceId)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || corpseLedger == null) return false;
            if (!corpseLedger.IsItemAvailable(corpseId, itemInstanceId)) return false;
            if (!profile.Recovery.TryGetState(itemInstanceId, out var state)) return false;
            if (state != ExtractionRecoveryItemState.Recoverable) return false;

            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.Lost,
                    ExtractionInventoryContainerType.Holding))
            {
                return false;
            }

            if (!corpseLedger.TryMarkLooted(corpseId, itemInstanceId))
            {
                profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.Holding,
                    ExtractionInventoryContainerType.Lost);
                return false;
            }

            if (profile.Recovery.TryMarkRecovered(itemInstanceId)) return true;

            profile.Ownership.TryMove(
                itemInstanceId,
                ExtractionInventoryContainerType.Holding,
                ExtractionInventoryContainerType.Lost);
            return false;
        }
    }
}
