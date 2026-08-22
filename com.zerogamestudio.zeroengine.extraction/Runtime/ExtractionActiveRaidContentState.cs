using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionActiveRaidContentState
    {
        public List<string> OpenedContainerIds = new();
        public List<string> OpenedLockIds = new();
        public List<string> ActivatedLeverIds = new();
        public List<string> UsedExtractionPointIds = new();
        public List<string> WorldPickupItemInstanceIds = new();
        public List<string> AppliedReceiptIds = new();
        public int DeadlineExtensionSeconds;
        public int ThreatLevelDelta;
        public List<string> UnlockedBonusContainerGroupIds = new();
        public List<ExtractionPointRuntimeState> ExtractionPointStates = new();
        public ExtractionRaidLootManifest LootManifest;

        // C2/C3 raid snapshot state. Existing UsedExtractionPointIds and
        // OpenedLockIds remain the persisted stores for those compatible IDs.
        public List<string> TriggeredMilestoneIds = new();
        public List<string> ActivatedGateIds = new();
        public List<string> OpenedKeyDoorIds = new();
        public List<ExtractionGateRewardState> GateRewardStates = new();
        public string SelectedRandomExtractionPointId;
        public int CurrentReinforcementLevel;
        public bool IsOvertime;
        public bool OvertimeEnemySpawned;

        public void EnsureInitialized()
        {
            OpenedContainerIds ??= new List<string>();
            OpenedLockIds ??= new List<string>();
            ActivatedLeverIds ??= new List<string>();
            UsedExtractionPointIds ??= new List<string>();
            WorldPickupItemInstanceIds ??= new List<string>();
            AppliedReceiptIds ??= new List<string>();
            UnlockedBonusContainerGroupIds ??= new List<string>();
            ExtractionPointStates ??= new List<ExtractionPointRuntimeState>();
            TriggeredMilestoneIds ??= new List<string>();
            ActivatedGateIds ??= new List<string>();
            OpenedKeyDoorIds ??= new List<string>();
            GateRewardStates ??= new List<ExtractionGateRewardState>();
            LootManifest?.EnsureInitialized();
        }
    }
}
