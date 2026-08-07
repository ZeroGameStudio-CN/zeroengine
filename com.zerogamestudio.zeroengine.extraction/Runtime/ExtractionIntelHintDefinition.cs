using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionIntelHintDefinition
    {
        public string HintId;
        public string MapId;
        public int RequiredIntelLevel;
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        public bool IsValid =>
            !string.IsNullOrEmpty(HintId)
            && !string.IsNullOrEmpty(MapId)
            && RequiredIntelLevel > 0;

        public ExtractionIntelHintDefinition(
            string hintId,
            string mapId,
            int requiredIntelLevel)
        {
            HintId = hintId;
            MapId = mapId;
            RequiredIntelLevel = requiredIntelLevel;
        }
    }
}
