using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemInstance
    {
        public string InstanceId;
        public string DefinitionId;
        public int Quantity;
        public string SourceKind;
        public string SourceId;

        public ExtractionItemInstance(string instanceId, string definitionId, int quantity)
            : this(instanceId, definitionId, quantity, null, null)
        {
        }

        public ExtractionItemInstance(
            string instanceId,
            string definitionId,
            int quantity,
            string sourceKind,
            string sourceId)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Quantity = quantity;
            SourceKind = sourceKind;
            SourceId = sourceId;
        }
    }
}
