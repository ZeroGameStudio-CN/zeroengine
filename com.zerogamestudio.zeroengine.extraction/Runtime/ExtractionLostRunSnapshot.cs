using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionLostRunSnapshot
    {
        public string SnapshotId;
        public string MapId;
        public int RaidSeed;
        public long DeathUnixSeconds;
        public string DeathReason;
        public string DeathRegion;
        public int DangerLevel;
        public int CorpseSeed;
        public List<string> LostItemInstanceIds = new();
        public List<string> LoadoutItemInstanceIds = new();
        public List<string> BackpackItemInstanceIds = new();
        public List<string> SecureItemInstanceIds = new();
        public List<string> RecoverableCandidateItemInstanceIds = new();

        public ExtractionLostRunSnapshot(string snapshotId, string mapId)
        {
            SnapshotId = snapshotId;
            MapId = mapId;
        }

        public void ApplyContext(ExtractionFailureContext context)
        {
            if (context == null) return;

            DeathUnixSeconds = context.DeathUnixSeconds;
            DeathReason = context.DeathReason;
            DeathRegion = context.DeathRegion;
            DangerLevel = context.DangerLevel;
            CorpseSeed = context.CorpseSeed;
        }
    }
}
