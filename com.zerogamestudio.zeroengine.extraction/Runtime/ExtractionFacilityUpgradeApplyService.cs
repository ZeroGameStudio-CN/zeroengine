namespace POB.Extraction
{
    public static class ExtractionFacilityUpgradeApplyService
    {
        public static bool TryApplyUpgrade(
            ExtractionProfileSaveData profile,
            ExtractionFacilityUpgradePlan plan)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || plan == null) return false;
            if (plan.TargetLevel != plan.CurrentLevel + 1) return false;

            profile.EnsureInitialized();
            int currentLevel = profile.Facilities.GetLevel(plan.FacilityType);
            if (currentLevel != plan.CurrentLevel) return false;

            return profile.Facilities.TrySetLevel(plan.FacilityType, plan.TargetLevel);
        }
    }
}
