using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionTrainingPassiveService
    {
        public static bool TryGetAvailablePassives(
            ExtractionProfileSaveData profile,
            List<ExtractionTrainingPassiveDefinition> definitions,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionTrainingPassiveDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || definitions == null) return false;
            profile.EnsureInitialized();

            int trainingLevel = profile.Facilities.GetLevel(ExtractionFacilityType.Training);
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.IsValid) continue;
                if (trainingLevel < definition.RequiredTrainingLevel) continue;
                if (!MeetsMetaRequirements(definition, metaProgress)) continue;

                results.Add(definition);
            }

            return results.Count > 0;
        }

        private static bool MeetsMetaRequirements(
            ExtractionTrainingPassiveDefinition definition,
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
