using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionSafeDefinition
    {
        public string SafeId;
        public string MapId;
        public string LootTableId;
        public string RequiredKeyItemDefinitionId;

        public bool IsValid =>
            !string.IsNullOrEmpty(SafeId)
            && !string.IsNullOrEmpty(MapId)
            && !string.IsNullOrEmpty(LootTableId);

        public bool RequiresKey => !string.IsNullOrEmpty(RequiredKeyItemDefinitionId);

        public ExtractionSafeDefinition(
            string safeId,
            string mapId,
            string lootTableId,
            string requiredKeyItemDefinitionId)
        {
            SafeId = safeId;
            MapId = mapId;
            LootTableId = lootTableId;
            RequiredKeyItemDefinitionId = requiredKeyItemDefinitionId;
        }

        public bool CanOpenWith(string keyItemDefinitionId)
        {
            return !RequiresKey || keyItemDefinitionId == RequiredKeyItemDefinitionId;
        }
    }
}
