using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionCharacterConfig
    {
        public float DefaultSearchSpeedMultiplier = 1f;
        public List<ExtractionCharacterAttributeDefinition> AttributeDefinitions = new();
        public List<ExtractionCharacterTalentDefinition> TalentDefinitions = new();
        public List<ExtractionCharacterInjuryDefinition> InjuryDefinitions = new();
        public List<string> EquipmentSlotIds = new();
        public List<string> BaselineAbilityIds = new();

        public bool TryGetAttributeDefinition(
            string attributeId,
            out ExtractionCharacterAttributeDefinition definition)
        {
            if (AttributeDefinitions != null)
            {
                foreach (var candidate in AttributeDefinitions)
                {
                    if (candidate != null && candidate.AttributeId == attributeId)
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetTalentDefinition(
            string talentId,
            out ExtractionCharacterTalentDefinition definition)
        {
            if (TalentDefinitions != null)
            {
                foreach (var candidate in TalentDefinitions)
                {
                    if (candidate != null && candidate.TalentId == talentId)
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }

            definition = null;
            return false;
        }

        public bool TryGetInjuryDefinition(
            string injuryId,
            out ExtractionCharacterInjuryDefinition definition)
        {
            if (InjuryDefinitions != null)
            {
                foreach (var candidate in InjuryDefinitions)
                {
                    if (candidate != null && candidate.InjuryId == injuryId)
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }

            definition = null;
            return false;
        }
    }

    [Serializable]
    public class ExtractionCharacterAttributeDefinition
    {
        public string AttributeId;
        public float DefaultValue;
        public float MinValue;
        public float MaxValue;

        public ExtractionCharacterAttributeDefinition(
            string attributeId,
            float defaultValue,
            float minValue,
            float maxValue)
        {
            AttributeId = attributeId;
            DefaultValue = defaultValue;
            MinValue = minValue;
            MaxValue = maxValue;
        }
    }

    [Serializable]
    public class ExtractionCharacterTalentDefinition
    {
        public string TalentId;

        public ExtractionCharacterTalentDefinition(string talentId)
        {
            TalentId = talentId;
        }
    }

    [Serializable]
    public class ExtractionCharacterInjuryDefinition
    {
        public string InjuryId;
        public int MaxSeverity;

        public ExtractionCharacterInjuryDefinition(string injuryId, int maxSeverity)
        {
            InjuryId = injuryId;
            MaxSeverity = maxSeverity;
        }
    }
}
