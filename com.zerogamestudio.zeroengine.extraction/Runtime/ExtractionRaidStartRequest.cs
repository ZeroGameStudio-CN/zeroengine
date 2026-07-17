using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRaidStartRequest
    {
        public string RaidId;
        public int Seed;
        public long StartedAtUnixSeconds;
        public List<string> LoadoutItemInstanceIds = new();
        public List<string> SecureItemInstanceIds = new();

        public bool IsValid => !string.IsNullOrEmpty(RaidId);

        public ExtractionRaidStartRequest(string raidId, int seed, long startedAtUnixSeconds)
        {
            RaidId = raidId;
            Seed = seed;
            StartedAtUnixSeconds = startedAtUnixSeconds;
        }
    }
}
