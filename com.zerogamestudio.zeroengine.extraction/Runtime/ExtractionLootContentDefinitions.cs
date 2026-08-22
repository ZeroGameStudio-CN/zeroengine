using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionRarityFloatValues
    {
        public float Common;
        public float Uncommon;
        public float Rare;
        public float Epic;
        public float Legendary;
        public float Mythic;

        public ExtractionRarityFloatValues()
        {
        }

        public ExtractionRarityFloatValues(
            float common,
            float uncommon,
            float rare,
            float epic,
            float legendary,
            float mythic)
        {
            Common = common;
            Uncommon = uncommon;
            Rare = rare;
            Epic = epic;
            Legendary = legendary;
            Mythic = mythic;
        }

        public float Get(ExtractionItemRarity rarity)
        {
            switch (rarity)
            {
                case ExtractionItemRarity.Common: return Common;
                case ExtractionItemRarity.Uncommon: return Uncommon;
                case ExtractionItemRarity.Rare: return Rare;
                case ExtractionItemRarity.Epic: return Epic;
                case ExtractionItemRarity.Legendary: return Legendary;
                case ExtractionItemRarity.Mythic: return Mythic;
                default: return 0f;
            }
        }
    }

    [Serializable]
    public class ExtractionRarityIntValues
    {
        public int Common;
        public int Uncommon;
        public int Rare;
        public int Epic;
        public int Legendary;
        public int Mythic;

        public ExtractionRarityIntValues()
        {
        }

        public ExtractionRarityIntValues(
            int common,
            int uncommon,
            int rare,
            int epic,
            int legendary,
            int mythic)
        {
            Common = common;
            Uncommon = uncommon;
            Rare = rare;
            Epic = epic;
            Legendary = legendary;
            Mythic = mythic;
        }

        public int Get(ExtractionItemRarity rarity)
        {
            switch (rarity)
            {
                case ExtractionItemRarity.Common: return Common;
                case ExtractionItemRarity.Uncommon: return Uncommon;
                case ExtractionItemRarity.Rare: return Rare;
                case ExtractionItemRarity.Epic: return Epic;
                case ExtractionItemRarity.Legendary: return Legendary;
                case ExtractionItemRarity.Mythic: return Mythic;
                default: return 0;
            }
        }
    }

    [Serializable]
    public class ExtractionContentTierDefinition
    {
        public string ContentTierId;
        public ExtractionRarityFloatValues RarityWeightMultipliers;

        public ExtractionContentTierDefinition(
            string contentTierId,
            ExtractionRarityFloatValues rarityWeightMultipliers)
        {
            ContentTierId = contentTierId;
            RarityWeightMultipliers = rarityWeightMultipliers;
        }
    }

    [Serializable]
    public class ExtractionLootPityDefinition
    {
        public ExtractionItemRarity TargetRarity;
        public float WeightMultiplierIncrementPerMiss;
        public float MaximumWeightMultiplier;

        public ExtractionLootPityDefinition(
            ExtractionItemRarity targetRarity,
            float weightMultiplierIncrementPerMiss,
            float maximumWeightMultiplier)
        {
            TargetRarity = targetRarity;
            WeightMultiplierIncrementPerMiss = weightMultiplierIncrementPerMiss;
            MaximumWeightMultiplier = maximumWeightMultiplier;
        }
    }

    [Serializable]
    public class ExtractionRevealTimeDefinition
    {
        public ExtractionRarityFloatValues BaseRevealSeconds;

        public ExtractionRevealTimeDefinition(ExtractionRarityFloatValues baseRevealSeconds)
        {
            BaseRevealSeconds = baseRevealSeconds;
        }
    }

    [Serializable]
    public class ExtractionLootProfileDefinition
    {
        public string LootProfileId;
        public string ContentTierId;
        public ExtractionRarityIntValues MinimumGeneratedDropsByRarity;
        public ExtractionLootPityDefinition Pity;
        public ExtractionRevealTimeDefinition RevealTimes;
        public List<string> RegionIds = new();

        public ExtractionLootProfileDefinition(string lootProfileId, string contentTierId)
        {
            LootProfileId = lootProfileId;
            ContentTierId = contentTierId;
        }
    }

    [Serializable]
    public class ExtractionLootRegionDefinition
    {
        public string RegionId;
        public ExtractionRarityFloatValues RarityWeightMultipliers;
        public List<string> AllowedContainerTypeIds = new();
        public List<string> ContainerSpawnIds = new();

        public ExtractionLootRegionDefinition(
            string regionId,
            ExtractionRarityFloatValues rarityWeightMultipliers)
        {
            RegionId = regionId;
            RarityWeightMultipliers = rarityWeightMultipliers;
        }
    }

    [Serializable]
    public class ExtractionContainerDefinition
    {
        public string ContainerTypeId;
        public int Capacity;
        public int MinimumContentCount;
        public int MaximumContentCount;
        public float SearchTimeMultiplier;
        public bool IsSpecial;
        public List<string> LootTableIds = new();

        public ExtractionContainerDefinition(
            string containerTypeId,
            int capacity,
            int minimumContentCount,
            int maximumContentCount,
            float searchTimeMultiplier)
        {
            ContainerTypeId = containerTypeId;
            Capacity = capacity;
            MinimumContentCount = minimumContentCount;
            MaximumContentCount = maximumContentCount;
            SearchTimeMultiplier = searchTimeMultiplier;
        }
    }

    [Serializable]
    public class ExtractionWeightedContainerCandidate
    {
        public string ContainerTypeId;
        public int Weight;
        public int DifficultyLevel;

        public ExtractionWeightedContainerCandidate(string containerTypeId, int weight)
        {
            ContainerTypeId = containerTypeId;
            Weight = weight;
        }

        public ExtractionWeightedContainerCandidate(
            string containerTypeId,
            int weight,
            int difficultyLevel)
            : this(containerTypeId, weight)
        {
            DifficultyLevel = difficultyLevel;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(ContainerTypeId)
            && DifficultyLevel >= 0
            && Weight > 0;
    }

    [Serializable]
    public class ExtractionContainerSpawnDefinition
    {
        public string SpawnId;
        public string RegionId;
        public List<ExtractionWeightedContainerCandidate> Candidates = new();
        public bool Always;
        public bool ChancePerRaid;
        public float Chance;
        public string BonusGroupId;
        public ExtractionLootPointKind PointKind = ExtractionLootPointKind.Normal;

        public ExtractionContainerSpawnDefinition(
            string spawnId,
            string regionId,
            bool always,
            bool chancePerRaid,
            float chance,
            ExtractionLootPointKind pointKind = ExtractionLootPointKind.Normal)
        {
            SpawnId = spawnId;
            RegionId = regionId;
            Always = always;
            ChancePerRaid = chancePerRaid;
            Chance = chance;
            PointKind = pointKind;
        }
    }

    public static class ExtractionLootContentPolicy
    {
        public static bool IsRarityEnabled(ExtractionItemRarity rarity, bool rareLootDisabled)
        {
            return !rareLootDisabled || (int)rarity < (int)ExtractionItemRarity.Rare;
        }

        public static bool IsLootTableEnabled(ExtractionLootTableDefinition table, bool rareLootDisabled)
        {
            return table != null && (!rareLootDisabled || !table.IsRare);
        }

        public static int GetMinimumGeneratedDrops(
            ExtractionLootProfileDefinition profile,
            ExtractionItemRarity rarity,
            bool rareLootDisabled)
        {
            if (profile == null || profile.MinimumGeneratedDropsByRarity == null)
                return 0;
            return IsRarityEnabled(rarity, rareLootDisabled)
                ? profile.MinimumGeneratedDropsByRarity.Get(rarity)
                : 0;
        }

        public static bool IsPityEnabled(ExtractionLootProfileDefinition profile, bool rareLootDisabled)
        {
            return !rareLootDisabled && profile != null && profile.Pity != null;
        }
    }
}
