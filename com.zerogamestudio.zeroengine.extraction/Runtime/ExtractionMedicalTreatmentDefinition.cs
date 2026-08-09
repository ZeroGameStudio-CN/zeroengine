using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionMedicalTreatmentDefinition
    {
        public string TreatmentId;
        public string ConditionId;
        public string CostItemDefinitionId;
        public int CostQuantity;
        public int RequiredMedicalLevel;
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        public bool IsValid =>
            !string.IsNullOrEmpty(TreatmentId)
            && !string.IsNullOrEmpty(ConditionId)
            && !string.IsNullOrEmpty(CostItemDefinitionId)
            && CostQuantity > 0
            && RequiredMedicalLevel > 0;

        public ExtractionMedicalTreatmentDefinition(
            string treatmentId,
            string conditionId,
            string costItemDefinitionId,
            int costQuantity,
            int requiredMedicalLevel)
        {
            TreatmentId = treatmentId;
            ConditionId = conditionId;
            CostItemDefinitionId = costItemDefinitionId;
            CostQuantity = costQuantity;
            RequiredMedicalLevel = requiredMedicalLevel;
        }
    }
}
