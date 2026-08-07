using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionTrainingPassiveDefinition
    {
        public string PassiveId;
        public int RequiredTrainingLevel;
        public int IntValue;
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        public bool IsValid =>
            !string.IsNullOrEmpty(PassiveId)
            && RequiredTrainingLevel > 0;

        public ExtractionTrainingPassiveDefinition(
            string passiveId,
            int requiredTrainingLevel,
            int intValue)
        {
            PassiveId = passiveId;
            RequiredTrainingLevel = requiredTrainingLevel;
            IntValue = intValue;
        }
    }
}
