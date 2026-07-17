using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionLootPickup
    {
        public ExtractionItemInstance Item;
        public ExtractionItemDefinition Definition;
        public bool CanEnterSecureContainer;

        public bool IsValid => Item != null && Definition != null && !string.IsNullOrEmpty(Item.InstanceId);

        public ExtractionLootPickup(
            ExtractionItemInstance item,
            ExtractionItemDefinition definition,
            bool canEnterSecureContainer)
        {
            Item = item;
            Definition = definition;
            CanEnterSecureContainer = canEnterSecureContainer;
        }
    }
}
