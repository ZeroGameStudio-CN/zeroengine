using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionFacilityUpgradeTransactionService
    {
        public static bool TryCompleteUpgrade(
            ExtractionProfileSaveData profile,
            ExtractionFacilityUpgradePlan plan,
            List<ExtractionInventoryContainerType> sourceContainers,
            IExtractionItemCatalog itemCatalog = null)
        {
            if (!CanApplyPlan(profile, plan)) return false;
            if (!TryConvertCosts(plan.Costs, out var costs)) return false;

            ExtractionCostConsumptionReceipt costReceipt = null;
            if (costs.Count > 0)
            {
                if (!ExtractionItemCostService.HasEnoughItems(profile, costs, sourceContainers)) return false;
                if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, sourceContainers, out costReceipt)) return false;
            }

            if (ExtractionFacilityUpgradeApplyService.TryApplyUpgrade(profile, plan)) return true;

            if (costReceipt != null)
                ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }

        private static bool CanApplyPlan(
            ExtractionProfileSaveData profile,
            ExtractionFacilityUpgradePlan plan)
        {
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || plan == null) return false;
            if (plan.TargetLevel != plan.CurrentLevel + 1) return false;

            profile.EnsureInitialized();
            return profile.Facilities.GetLevel(plan.FacilityType) == plan.CurrentLevel;
        }

        private static bool TryConvertCosts(
            List<ExtractionFacilityUpgradeCost> source,
            out List<ExtractionItemCost> costs)
        {
            costs = new List<ExtractionItemCost>();
            if (source == null || source.Count == 0) return true;

            foreach (var cost in source)
            {
                if (cost == null || !cost.IsValid) return false;
                costs.Add(new ExtractionItemCost(cost.ItemDefinitionId, cost.Quantity));
            }

            return true;
        }
    }
}
