using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionRaidLootManifestFailure
    {
        None = 0,
        InvalidInput = 1,
        MissingContentConfiguration = 2,
        NoSpawnedContainers = 3,
        InsufficientGuaranteedCapacity = 4,
        MissingRarityCandidate = 5
    }

    public enum ExtractionContainerLootEntryState
    {
        CommittedHidden = 0,
        Revealed = 1,
        Transferred = 2
    }

    [Serializable]
    public class ExtractionLootPityState
    {
        public int ConsecutiveMisses;
    }

    [Serializable]
    public class ExtractionContainerSearchState
    {
        public string ContainerId;
        public string ResultReceiptId;
        public int CurrentRevealOrder;
        public float CurrentEntryElapsedSeconds;
        public bool Paused = true;
        public bool Completed;

        public ExtractionContainerSearchState(string containerId)
        {
            ContainerId = containerId;
        }
    }

    [Serializable]
    public class ExtractionContainerLootEntry
    {
        public string EntryId;
        public string ItemInstanceId;
        public string DefinitionId;
        public int Quantity;
        public ExtractionItemRarity Rarity;
        public bool Guaranteed;
        public int RevealOrder = -1;
        public ExtractionContainerLootEntryState State = ExtractionContainerLootEntryState.CommittedHidden;
        public string RevealReceiptId;
        public string TransferReceiptId;

        public ExtractionContainerLootEntry(
            string entryId,
            string itemInstanceId,
            string definitionId,
            int quantity,
            ExtractionItemRarity rarity,
            bool guaranteed)
        {
            EntryId = entryId;
            ItemInstanceId = itemInstanceId;
            DefinitionId = definitionId;
            Quantity = quantity;
            Rarity = rarity;
            Guaranteed = guaranteed;
        }
    }

    [Serializable]
    public class ExtractionRaidContainerManifest
    {
        public string ContainerId;
        public string RegionId;
        public string ContainerTypeId;
        public int Capacity;
        public int TargetContentCount;
        public int MaximumContentCount;
        public float SearchTimeMultiplier = 1f;
        public string BonusGroupId;
        public bool Active = true;
        public bool Opened;
        public int OpenSequence = -1;
        public string OpenReceiptId;
        public List<ExtractionContainerLootEntry> Entries = new();
        public ExtractionContainerSearchState SearchState;

        public ExtractionRaidContainerManifest(
            string containerId,
            string regionId,
            string containerTypeId,
            int capacity,
            int targetContentCount,
            int maximumContentCount,
            float searchTimeMultiplier)
        {
            ContainerId = containerId;
            RegionId = regionId;
            ContainerTypeId = containerTypeId;
            Capacity = capacity;
            TargetContentCount = targetContentCount;
            MaximumContentCount = maximumContentCount;
            SearchTimeMultiplier = searchTimeMultiplier;
            SearchState = new ExtractionContainerSearchState(containerId);
        }

        internal void EnsureInitialized()
        {
            Entries ??= new List<ExtractionContainerLootEntry>();
            SearchState ??= new ExtractionContainerSearchState(ContainerId);
        }
    }

    [Serializable]
    public class ExtractionRaidLootManifest
    {
        public string ManifestId;
        public string LootProfileId;
        public string ContentTierId;
        public int RaidSeed;
        public bool RareLootDisabled;
        public int NextOpenSequence;
        public string ActiveSearchContainerId;
        public ExtractionLootPityState PityState = new();
        public List<ExtractionRaidContainerManifest> Containers = new();

        public bool TryGetContainer(string containerId, out ExtractionRaidContainerManifest container)
        {
            if (!string.IsNullOrEmpty(containerId) && Containers != null)
            {
                foreach (var candidate in Containers)
                {
                    if (candidate != null && candidate.ContainerId == containerId)
                    {
                        container = candidate;
                        return true;
                    }
                }
            }

            container = null;
            return false;
        }

        internal void EnsureInitialized()
        {
            PityState ??= new ExtractionLootPityState();
            Containers ??= new List<ExtractionRaidContainerManifest>();
            foreach (var container in Containers)
                container?.EnsureInitialized();
        }
    }
}
