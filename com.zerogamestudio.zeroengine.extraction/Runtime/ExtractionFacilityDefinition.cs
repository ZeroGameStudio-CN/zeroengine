using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFacilityEffect
    {
        public string EffectId;
        public int IntValue;

        public bool IsValid => !string.IsNullOrEmpty(EffectId);

        public ExtractionFacilityEffect(string effectId, int intValue)
        {
            EffectId = effectId;
            IntValue = intValue;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFacilityUpgradeCost
    {
        public string ItemDefinitionId;
        public int Quantity;

        public bool IsValid => !string.IsNullOrEmpty(ItemDefinitionId) && Quantity > 0;

        public ExtractionFacilityUpgradeCost(string itemDefinitionId, int quantity)
        {
            ItemDefinitionId = itemDefinitionId;
            Quantity = quantity;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFacilityDefinition
    {
        public ExtractionFacilityType FacilityType;
        public int Level;
        public List<ExtractionFacilityEffect> Effects = new();
        public List<ExtractionFacilityUpgradeCost> UpgradeCosts = new();
        public string RequiredSharedUnlockId;
        public int RequiredSharedUnlockLevel;
        public string RequiredSharedTalentId;

        public bool IsValid
        {
            get
            {
                if (Level <= 0 || Effects == null || Effects.Count == 0) return false;
                foreach (var effect in Effects)
                {
                    if (effect == null || !effect.IsValid) return false;
                }

                return true;
            }
        }

        public ExtractionFacilityDefinition(ExtractionFacilityType facilityType, int level)
        {
            FacilityType = facilityType;
            Level = level;
        }
    }
}
