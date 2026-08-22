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
        public List<ExtractionEquipmentSlotState> EquipmentSlots = new();
        public List<string> OpenedSafeIds = new();
        public List<string> RaidFlagIds = new();
        public List<string> LootedSourceIds = new();
        public ExtractionActiveRaidContentState Content = new();

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
            if (request.EquipmentSlots != null)
            {
                foreach (var slot in request.EquipmentSlots)
                {
                    if (slot == null) continue;
                    EquipmentSlots.Add(
                        new ExtractionEquipmentSlotState(
                            slot.SlotId,
                            slot.ItemInstanceId,
                            slot.EffectReceiptId));
                }
            }
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

        public bool HasUsedExtractionPoint(string pointId)
        {
            return Contains(Content?.UsedExtractionPointIds, pointId);
        }

        public bool MarkExtractionPointUsed(string pointId)
        {
            EnsureInitialized();
            return Mark(Content.UsedExtractionPointIds, pointId);
        }

        public bool TryGetExtractionPointRuntimeState(
            string pointId,
            out ExtractionPointRuntimeState state)
        {
            state = null;
            if (string.IsNullOrEmpty(pointId) || Content?.ExtractionPointStates == null)
                return false;
            foreach (var candidate in Content.ExtractionPointStates)
            {
                if (candidate != null && candidate.PointId == pointId)
                {
                    state = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool HasExtractionPointOpened(string pointId)
        {
            return TryGetExtractionPointRuntimeState(pointId, out var state)
                && state.OpenedAtElapsedSeconds >= 0;
        }

        public bool TryMarkExtractionPointOpened(string pointId, int openedAtElapsedSeconds)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(pointId) || openedAtElapsedSeconds < 0)
                return false;
            if (TryGetExtractionPointRuntimeState(pointId, out var existing))
            {
                if (existing.OpenedAtElapsedSeconds >= 0) return false;
                existing.OpenedAtElapsedSeconds = openedAtElapsedSeconds;
                return true;
            }

            Content.ExtractionPointStates.Add(
                new ExtractionPointRuntimeState(pointId, 0, openedAtElapsedSeconds));
            return true;
        }

        public bool HasActivatedGate(string gateId)
        {
            return Contains(Content?.ActivatedGateIds, gateId);
        }

        public bool MarkGateActivated(string gateId)
        {
            EnsureInitialized();
            return Mark(Content.ActivatedGateIds, gateId);
        }

        public bool HasOpenedKeyDoor(string doorId)
        {
            return Contains(Content?.OpenedKeyDoorIds, doorId);
        }

        public bool MarkKeyDoorOpened(string doorId)
        {
            EnsureInitialized();
            return Mark(Content.OpenedKeyDoorIds, doorId);
        }

        public bool HasRewardedGate(string gateId)
        {
            return TryGetGateRewardState(gateId, out _);
        }

        public bool TryGetGateRewardState(
            string gateId,
            out ExtractionGateRewardState state)
        {
            state = null;
            if (string.IsNullOrEmpty(gateId) || Content?.GateRewardStates == null)
                return false;
            foreach (var candidate in Content.GateRewardStates)
            {
                if (candidate != null && candidate.GateId == gateId)
                {
                    state = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryMarkGateRewarded(string gateId, string containerSpawnId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(gateId) || string.IsNullOrEmpty(containerSpawnId))
                return false;
            if (TryGetGateRewardState(gateId, out _)) return false;
            Content.GateRewardStates.Add(new ExtractionGateRewardState(gateId, containerSpawnId));
            return true;
        }

        public bool HasTriggeredMilestone(string milestoneId)
        {
            return Contains(Content?.TriggeredMilestoneIds, milestoneId);
        }

        public bool MarkMilestoneTriggered(string milestoneId)
        {
            EnsureInitialized();
            return Mark(Content.TriggeredMilestoneIds, milestoneId);
        }

        private static bool Contains(List<string> values, string value)
        {
            return !string.IsNullOrEmpty(value)
                && values != null
                && values.Contains(value);
        }

        private static bool Mark(List<string> values, string value)
        {
            if (string.IsNullOrEmpty(value) || values == null) return false;
            if (values.Contains(value)) return false;
            values.Add(value);
            return true;
        }

        public void EnsureInitialized()
        {
            LoadoutItemInstanceIds ??= new List<string>();
            SecureItemInstanceIds ??= new List<string>();
            EquipmentSlots ??= new List<ExtractionEquipmentSlotState>();
            OpenedSafeIds ??= new List<string>();
            RaidFlagIds ??= new List<string>();
            LootedSourceIds ??= new List<string>();
            Content ??= new ExtractionActiveRaidContentState();
            Content.EnsureInitialized();
        }
    }
}
