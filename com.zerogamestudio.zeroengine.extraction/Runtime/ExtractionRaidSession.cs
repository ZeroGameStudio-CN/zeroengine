using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionRaidSession
    {
        public string RaidId;
        public string MapId;
        public string RaidRoomName;
        public int Seed;
        public long StartedAtUnixSeconds;
        public int DurationSeconds;
        public int ThreatLevel;
        public bool AllowEmergencyExtraction;
        public List<string> LoadoutItemInstanceIds = new();
        public List<string> SecureItemInstanceIds = new();
        public List<string> OpenedSafeIds = new();
        public List<string> RaidFlagIds = new();
        public List<string> LootedSourceIds = new();

        public ExtractionRaidSession(
            ExtractionMapDefinition map,
            ExtractionRaidStartRequest request)
        {
            RaidId = request.RaidId;
            MapId = map.MapId;
            RaidRoomName = map.RaidRoomName;
            Seed = request.Seed;
            StartedAtUnixSeconds = request.StartedAtUnixSeconds;
            DurationSeconds = map.DurationSeconds;
            ThreatLevel = map.ThreatLevel;
            AllowEmergencyExtraction = map.AllowEmergencyExtraction;
            LoadoutItemInstanceIds.AddRange(request.LoadoutItemInstanceIds);
            SecureItemInstanceIds.AddRange(request.SecureItemInstanceIds);
        }

        public bool HasOpenedSafe(string safeId)
        {
            return !string.IsNullOrEmpty(safeId)
                && OpenedSafeIds != null
                && OpenedSafeIds.Contains(safeId);
        }

        public void MarkSafeOpened(string safeId)
        {
            if (string.IsNullOrEmpty(safeId)) return;

            if (OpenedSafeIds == null)
                OpenedSafeIds = new List<string>();

            if (!OpenedSafeIds.Contains(safeId))
                OpenedSafeIds.Add(safeId);
        }

        public bool HasLootedSource(string sourceId)
        {
            return !string.IsNullOrEmpty(sourceId)
                && LootedSourceIds != null
                && LootedSourceIds.Contains(sourceId);
        }

        public void MarkLootedSource(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return;

            if (LootedSourceIds == null)
                LootedSourceIds = new List<string>();

            if (!LootedSourceIds.Contains(sourceId))
                LootedSourceIds.Add(sourceId);
        }

        public bool HasRaidFlag(string flagId)
        {
            return !string.IsNullOrEmpty(flagId)
                && RaidFlagIds != null
                && RaidFlagIds.Contains(flagId);
        }

        public void MarkRaidFlag(string flagId)
        {
            if (string.IsNullOrEmpty(flagId)) return;

            if (RaidFlagIds == null)
                RaidFlagIds = new List<string>();

            if (!RaidFlagIds.Contains(flagId))
                RaidFlagIds.Add(flagId);
        }
    }
}
