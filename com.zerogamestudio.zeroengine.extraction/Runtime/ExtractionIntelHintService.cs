using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionIntelHintService
    {
        public static bool TryGetAvailableHints(
            ExtractionProfileSaveData profile,
            List<ExtractionIntelHintDefinition> definitions,
            string mapId,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionIntelHintDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || definitions == null || string.IsNullOrEmpty(mapId)) return false;
            profile.EnsureInitialized();

            int intelLevel = profile.Facilities.GetLevel(ExtractionFacilityType.Intel);
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.IsValid) continue;
                if (definition.MapId != mapId) continue;
                if (intelLevel < definition.RequiredIntelLevel) continue;
                if (!MeetsMetaRequirements(definition, metaProgress)) continue;

                results.Add(definition);
            }

            return results.Count > 0;
        }

        private static bool MeetsMetaRequirements(
            ExtractionIntelHintDefinition definition,
            IExtractionMetaProgressProvider metaProgress)
        {
            metaProgress ??= new ExtractionNoOpMetaProgressProvider();

            if (!string.IsNullOrEmpty(definition.RequiredSharedUnlockId))
            {
                if (definition.RequiredSharedUnlockLevel <= 0) return false;
                if (metaProgress.GetSharedUnlockLevel(definition.RequiredSharedUnlockId) < definition.RequiredSharedUnlockLevel)
                    return false;
            }

            if (!string.IsNullOrEmpty(definition.RequiredSharedTalentId))
            {
                if (!metaProgress.HasSharedTalent(definition.RequiredSharedTalentId)) return false;
            }

            return true;
        }
    }
}
