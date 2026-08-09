using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionCharacterProfile
    {
        public List<ExtractionCharacterValue> AttributeValues = new();
        public List<string> UnlockedTalentIds = new();
        public List<ExtractionCharacterInjuryState> Injuries = new();
        public float SearchSpeedMultiplier = 1f;
        public List<string> BaselineAbilityIds = new();

        internal void EnsureInitialized()
        {
            AttributeValues ??= new List<ExtractionCharacterValue>();
            UnlockedTalentIds ??= new List<string>();
            Injuries ??= new List<ExtractionCharacterInjuryState>();
            BaselineAbilityIds ??= new List<string>();
        }
    }

    [Serializable]
    public class ExtractionCharacterValue
    {
        public string AttributeId;
        public float Value;

        public ExtractionCharacterValue(string attributeId, float value)
        {
            AttributeId = attributeId;
            Value = value;
        }
    }

    [Serializable]
    public class ExtractionCharacterInjuryState
    {
        public string InjuryId;
        public int Severity;

        public ExtractionCharacterInjuryState(string injuryId, int severity)
        {
            InjuryId = injuryId;
            Severity = severity;
        }
    }
}
