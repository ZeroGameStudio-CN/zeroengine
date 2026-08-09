using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMedicalTreatmentService
    {
        public static bool TryGetAvailableTreatments(
            ExtractionProfileSaveData profile,
            List<ExtractionMedicalTreatmentDefinition> definitions,
            IExtractionItemCatalog itemCatalog,
            string conditionId,
            IExtractionMetaProgressProvider metaProgress,
            List<ExtractionMedicalTreatmentDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || definitions == null || itemCatalog == null) return false;
            if (string.IsNullOrEmpty(conditionId)) return false;
            profile.EnsureInitialized();

            int medicalLevel = profile.Facilities.GetLevel(ExtractionFacilityType.Medical);
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.IsValid) continue;
                if (definition.ConditionId != conditionId) continue;
                if (medicalLevel < definition.RequiredMedicalLevel) continue;
                if (!itemCatalog.TryGetItemDefinition(definition.CostItemDefinitionId, out _)) continue;
                if (!MeetsMetaRequirements(definition, metaProgress)) continue;
                if (profile.MedicalTreatments.HasTreatedCondition(definition.ConditionId)) continue;

                results.Add(definition);
            }

            return results.Count > 0;
        }

        private static bool MeetsMetaRequirements(
            ExtractionMedicalTreatmentDefinition definition,
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
