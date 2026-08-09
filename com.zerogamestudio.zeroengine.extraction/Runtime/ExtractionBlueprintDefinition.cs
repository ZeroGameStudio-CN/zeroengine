using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionBlueprintDefinition
    {
        public string BlueprintId;
        public string CostItemDefinitionId;
        public int CostQuantity;

        public bool IsValid =>
            !string.IsNullOrEmpty(BlueprintId)
            && !string.IsNullOrEmpty(CostItemDefinitionId)
            && CostQuantity > 0;

        public ExtractionBlueprintDefinition(
            string blueprintId,
            string costItemDefinitionId,
            int costQuantity)
        {
            BlueprintId = blueprintId;
            CostItemDefinitionId = costItemDefinitionId;
            CostQuantity = costQuantity;
        }
    }
}
