using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    public enum ExtractionFacilityType
    {
        Stash,
        SecureContainer,
        Recovery,
        MapTable,
        Merchant,
        Crafting,
        Medical,
        Intel,
        Training
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFacilityProfile
    {
        public List<ExtractionFacilityState> Entries = new();

        public int GetLevel(ExtractionFacilityType facilityType)
        {
            var state = FindState(facilityType);
            return state?.Level ?? 0;
        }

        public bool TrySetLevel(ExtractionFacilityType facilityType, int level)
        {
            if (level <= 0) return false;

            var state = FindState(facilityType);
            if (state == null)
            {
                Entries.Add(new ExtractionFacilityState(facilityType, level));
                return true;
            }

            if (level > state.Level)
                state.Level = level;
            return true;
        }

        private ExtractionFacilityState FindState(ExtractionFacilityType facilityType)
        {
            foreach (var state in Entries)
            {
                if (state.FacilityType == facilityType) return state;
            }

            return null;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFacilityState
    {
        public ExtractionFacilityType FacilityType;
        public int Level;

        public ExtractionFacilityState(ExtractionFacilityType facilityType, int level)
        {
            FacilityType = facilityType;
            Level = level;
        }
    }
}
