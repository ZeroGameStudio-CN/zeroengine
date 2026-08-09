namespace POB.Extraction
{
    public static class ExtractionRecoveryService
    {
        public static bool TryRecoverCorpseItem(ExtractionProfileSaveData profile, string itemInstanceId)
        {
            return TryRecoverItem(profile, itemInstanceId);
        }

        public static bool TryRecoverBuybackItem(ExtractionProfileSaveData profile, string itemInstanceId)
        {
            return TryRecoverItem(profile, itemInstanceId);
        }

        private static bool TryRecoverItem(ExtractionProfileSaveData profile, string itemInstanceId)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || string.IsNullOrEmpty(itemInstanceId)) return false;
            if (!profile.Recovery.TryGetState(itemInstanceId, out var state)) return false;
            if (state != ExtractionRecoveryItemState.Recoverable) return false;

            if (!profile.Ownership.TryMove(
                    itemInstanceId,
                    ExtractionInventoryContainerType.Lost,
                    ExtractionInventoryContainerType.Holding))
            {
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
