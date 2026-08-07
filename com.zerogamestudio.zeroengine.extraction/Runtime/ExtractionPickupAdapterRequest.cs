using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionPickupAdapterRequest
    {
        public string InstanceId;
        public string DefinitionId;
        public int Quantity;
        public bool CanEnterSecureContainer;
        public string SourceKind;
        public string SourceId;

        public bool IsValid =>
            !string.IsNullOrEmpty(InstanceId)
            && !string.IsNullOrEmpty(DefinitionId)
            && Quantity > 0;

        public ExtractionPickupAdapterRequest(
            string instanceId,
            string definitionId,
            int quantity,
            bool canEnterSecureContainer,
            string sourceKind,
            string sourceId)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Quantity = quantity;
            CanEnterSecureContainer = canEnterSecureContainer;
            SourceKind = sourceKind;
            SourceId = sourceId;
        }
    }
}
