using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionMedicalTreatmentTransactionService
    {
        public static bool TryCompleteTreatment(
            ExtractionProfileSaveData profile,
            ExtractionMedicalTreatmentDefinition treatment,
            IExtractionItemCatalog itemCatalog,
            List<ExtractionInventoryContainerType> costSourceContainers)
        {
            if (!CanCompleteTreatment(profile, treatment, itemCatalog, out var costs)) return false;
            if (!ExtractionItemCostService.HasEnoughItems(profile, costs, costSourceContainers)) return false;
            if (!ExtractionItemCostService.TryConsumeCosts(profile, costs, costSourceContainers, out var costReceipt)) return false;

            if (profile.MedicalTreatments.Record(treatment.TreatmentId, treatment.ConditionId)) return true;

            ExtractionItemCostService.RestoreCosts(profile, costReceipt, itemCatalog);
            return false;
        }

        private static bool CanCompleteTreatment(
            ExtractionProfileSaveData profile,
            ExtractionMedicalTreatmentDefinition treatment,
            IExtractionItemCatalog itemCatalog,
            out List<ExtractionItemCost> costs)
        {
            costs = null;
            if (!ExtractionFeatureSwitch.Enabled) return false;
            if (profile == null || treatment == null || !treatment.IsValid || itemCatalog == null) return false;

            profile.EnsureInitialized();
            if (profile.Facilities.GetLevel(ExtractionFacilityType.Medical) < treatment.RequiredMedicalLevel)
                return false;
            if (!itemCatalog.TryGetItemDefinition(treatment.CostItemDefinitionId, out _)) return false;
            if (profile.MedicalTreatments.HasTreatedCondition(treatment.ConditionId)) return false;

            costs = new List<ExtractionItemCost>
            {
                new ExtractionItemCost(treatment.CostItemDefinitionId, treatment.CostQuantity)
            };
            return true;
        }
    }
}
