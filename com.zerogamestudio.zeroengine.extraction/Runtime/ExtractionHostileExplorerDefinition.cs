using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionHostileExplorerDefinition
    {
        public string EncounterId;
        public string MapId;
        public string ActorKey;
        public string LootTableId;
        public int MinThreatLevel;
        public int Weight;
        public string SpawnPointId;
        public int DifficultyLevel;
        public bool IsBoss;

        public bool IsValid =>
            !string.IsNullOrEmpty(EncounterId)
            && !string.IsNullOrEmpty(MapId)
            && !string.IsNullOrEmpty(ActorKey)
            && !string.IsNullOrEmpty(LootTableId)
            && MinThreatLevel >= 0
            && DifficultyLevel >= 0
            && Weight > 0;

        public ExtractionHostileExplorerDefinition(
            string encounterId,
            string mapId,
            string actorKey,
            string lootTableId,
            int minThreatLevel,
            int weight)
        {
            EncounterId = encounterId;
            MapId = mapId;
            ActorKey = actorKey;
            LootTableId = lootTableId;
            MinThreatLevel = minThreatLevel;
            Weight = weight;
        }
    }
}
