using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionFailureContext
    {
        public long DeathUnixSeconds;
        public string DeathReason;
        public string DeathRegion;
        public int DangerLevel;
        public int CorpseSeed;

        public ExtractionFailureContext(
            long deathUnixSeconds,
            string deathReason,
            string deathRegion,
            int dangerLevel,
            int corpseSeed)
        {
            DeathUnixSeconds = deathUnixSeconds;
            DeathReason = deathReason;
            DeathRegion = deathRegion;
            DangerLevel = dangerLevel;
            CorpseSeed = corpseSeed;
        }
    }
}
