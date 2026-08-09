using System.Collections.Generic;

namespace POB.Extraction
{
    public class ExtractionFacilityUpgradePlan
    {
        public ExtractionFacilityType FacilityType;
        public int CurrentLevel;
        public int TargetLevel;
        public List<ExtractionFacilityUpgradeCost> Costs = new();

        public ExtractionFacilityUpgradePlan(
            ExtractionFacilityType facilityType,
            int currentLevel,
            int targetLevel,
            List<ExtractionFacilityUpgradeCost> costs)
        {
            FacilityType = facilityType;
            CurrentLevel = currentLevel;
            TargetLevel = targetLevel;
            Costs = costs ?? new List<ExtractionFacilityUpgradeCost>();
        }
    }

    public static class ExtractionFacilityUpgradePlanService
    {
        public static bool TryGetNextUpgradePlan(
            ExtractionProfileSaveData profile,
            List<ExtractionFacilityDefinition> definitions,
            ExtractionFacilityType facilityType,
            IExtractionMetaProgressProvider metaProgress,
            out ExtractionFacilityUpgradePlan plan)
        {
            plan = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || definitions == null) return false;
            profile.EnsureInitialized();

            int currentLevel = profile.Facilities.GetLevel(facilityType);
            int targetLevel = currentLevel + 1;
            if (!TryGetTargetDefinition(definitions, facilityType, targetLevel, out var definition)) return false;
            if (!MeetsMetaRequirements(definition, metaProgress)) return false;
            if (!TryCopyCosts(definition.UpgradeCosts, out var costs)) return false;

            plan = new ExtractionFacilityUpgradePlan(
                facilityType,
                currentLevel,
                targetLevel,
                costs);
            return true;
        }

        private static bool TryGetTargetDefinition(
            List<ExtractionFacilityDefinition> definitions,
            ExtractionFacilityType facilityType,
            int targetLevel,
            out ExtractionFacilityDefinition definition)
        {
            definition = null;

            foreach (var candidate in definitions)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.FacilityType != facilityType) continue;
                if (candidate.Level != targetLevel) continue;

                definition = candidate;
                return true;
            }

            return false;
        }

        private static bool MeetsMetaRequirements(
            ExtractionFacilityDefinition definition,
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

        private static bool TryCopyCosts(
            List<ExtractionFacilityUpgradeCost> source,
            out List<ExtractionFacilityUpgradeCost> costs)
        {
            costs = new List<ExtractionFacilityUpgradeCost>();
            if (source == null || source.Count == 0) return true;

            foreach (var cost in source)
            {
                if (cost == null || !cost.IsValid) return false;
                costs.Add(new ExtractionFacilityUpgradeCost(cost.ItemDefinitionId, cost.Quantity));
            }

            return true;
        }
    }
}
