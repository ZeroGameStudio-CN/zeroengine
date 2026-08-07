namespace POB.Extraction
{
    public static class ExtractionBlueprintService
    {
        public static bool IsBlueprintUnlocked(ExtractionProfileSaveData profile, string blueprintId)
        {
            if (profile == null || string.IsNullOrEmpty(blueprintId)) return false;
            profile.EnsureInitialized();
            foreach (var unlockedBlueprintId in profile.UnlockedBlueprintIds)
            {
                if (unlockedBlueprintId == blueprintId) return true;
            }

            return false;
        }

        public static bool TryUnlockBlueprint(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string blueprintId)
        {
            if (profile == null || config == null || string.IsNullOrEmpty(blueprintId)) return false;
            if (!config.TryGetBlueprintDefinition(blueprintId, out var blueprint) || blueprint == null || !blueprint.IsValid)
                return false;

            profile.EnsureInitialized();
            if (IsBlueprintUnlocked(profile, blueprintId)) return true;

            profile.UnlockedBlueprintIds.Add(blueprintId);
            return true;
        }
    }
}
