using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionPointMode
    {
        Normal = 0,
        Timed = 1,
        Gate = 2,
        Boss = 3,
        Sacrifice = 4,
        Random = 5
    }

    public enum ExtractionSpawnPointKind
    {
        Enemy = 0,
        Boss = 1
    }

    public enum ExtractionLootPointKind
    {
        Normal = 0,
        Special = 1
    }

    public enum ExtractionGateMode
    {
        Capture = 0,
        Sacrifice = 1,
        Hold = 2
    }

    [Serializable]
    public class ExtractionEnemySpawnPointDefinition
    {
        public string PointId;
        public string MapId;
        public ExtractionSpawnPointKind PointKind;

        public ExtractionEnemySpawnPointDefinition()
        {
        }

        public ExtractionEnemySpawnPointDefinition(
            string pointId,
            string mapId,
            ExtractionSpawnPointKind pointKind)
        {
            PointId = pointId;
            MapId = mapId;
            PointKind = pointKind;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(PointId)
            && !string.IsNullOrEmpty(MapId)
            && Enum.IsDefined(typeof(ExtractionSpawnPointKind), PointKind);
    }

    [Serializable]
    public class ExtractionPointDifficultyCandidate
    {
        public string PointId;
        public int DifficultyLevel;
        public string ContentId;
        public int Weight;

        public ExtractionPointDifficultyCandidate()
        {
        }

        public ExtractionPointDifficultyCandidate(
            string pointId,
            int difficultyLevel,
            string contentId,
            int weight)
        {
            PointId = pointId;
            DifficultyLevel = difficultyLevel;
            ContentId = contentId;
            Weight = weight;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(PointId)
            && DifficultyLevel >= 0
            && !string.IsNullOrEmpty(ContentId)
            && Weight > 0;
    }

    [Serializable]
    public class ExtractionGateDefinition
    {
        public string GateId;
        public string MapId;
        public ExtractionGateMode Mode;
        public int ChannelSeconds;
        public int RequiredItemQuantity;
        public ExtractionItemRarity RequiredItemRarity;
        public string RequiredItemTagId;
        public List<string> RewardContainerSpawnIds = new();
        public string EnemyProfileId;
        public int EnemySpawnIntervalSeconds;

        public ExtractionGateDefinition()
        {
        }

        public ExtractionGateDefinition(
            string gateId,
            string mapId,
            ExtractionGateMode mode,
            int channelSeconds,
            int requiredItemQuantity,
            ExtractionItemRarity requiredItemRarity,
            string requiredItemTagId,
            IEnumerable<string> rewardContainerSpawnIds,
            string enemyProfileId,
            int enemySpawnIntervalSeconds = 0)
        {
            GateId = gateId;
            MapId = mapId;
            Mode = mode;
            ChannelSeconds = channelSeconds;
            RequiredItemQuantity = requiredItemQuantity;
            RequiredItemRarity = requiredItemRarity;
            RequiredItemTagId = requiredItemTagId;
            if (rewardContainerSpawnIds != null)
                RewardContainerSpawnIds.AddRange(rewardContainerSpawnIds);
            EnemyProfileId = enemyProfileId;
            EnemySpawnIntervalSeconds = enemySpawnIntervalSeconds;
        }

        public bool IsValid
        {
            get
            {
                bool hasTag = !string.IsNullOrEmpty(RequiredItemTagId);
                bool hasRequirement = RequiredItemQuantity > 0;
                return !string.IsNullOrEmpty(GateId)
                    && !string.IsNullOrEmpty(MapId)
                    && Enum.IsDefined(typeof(ExtractionGateMode), Mode)
                    && ChannelSeconds > 0
                    && RequiredItemQuantity >= 0
                    && Enum.IsDefined(typeof(ExtractionItemRarity), RequiredItemRarity)
                    && (!hasTag || hasRequirement)
                    && RewardContainerSpawnIds != null
                    && RewardContainerSpawnIds.Count > 0
                    && HasOnlyStableIds(RewardContainerSpawnIds)
                    && IsModeConfigurationValid(hasRequirement);
            }
        }

        private bool IsModeConfigurationValid(bool hasRequirement)
        {
            switch (Mode)
            {
                case ExtractionGateMode.Capture:
                    return !string.IsNullOrEmpty(EnemyProfileId)
                        && EnemySpawnIntervalSeconds > 0
                        && !hasRequirement
                        && string.IsNullOrEmpty(RequiredItemTagId);
                case ExtractionGateMode.Sacrifice:
                    return hasRequirement
                        && string.IsNullOrEmpty(EnemyProfileId)
                        && EnemySpawnIntervalSeconds == 0;
                case ExtractionGateMode.Hold:
                    return !hasRequirement
                        && string.IsNullOrEmpty(RequiredItemTagId)
                        && string.IsNullOrEmpty(EnemyProfileId)
                        && EnemySpawnIntervalSeconds == 0;
                default:
                    return false;
            }
        }

        private static bool HasOnlyStableIds(List<string> ids)
        {
            foreach (string id in ids)
                if (string.IsNullOrEmpty(id)) return false;
            return true;
        }
    }

    [Serializable]
    public class ExtractionKeyDoorDefinition
    {
        public string DoorId;
        public string MapId;
        public string LockDefinitionId;
        public string BindingId;

        public ExtractionKeyDoorDefinition()
        {
        }

        public ExtractionKeyDoorDefinition(
            string doorId,
            string mapId,
            string lockDefinitionId,
            string bindingId)
        {
            DoorId = doorId;
            MapId = mapId;
            LockDefinitionId = lockDefinitionId;
            BindingId = bindingId;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(DoorId)
            && !string.IsNullOrEmpty(MapId)
            && !string.IsNullOrEmpty(LockDefinitionId)
            && !string.IsNullOrEmpty(BindingId);
    }

    [Serializable]
    public class ExtractionGateRewardState
    {
        public string GateId;
        public string ContainerSpawnId;

        public ExtractionGateRewardState()
        {
        }

        public ExtractionGateRewardState(string gateId, string containerSpawnId)
        {
            GateId = gateId;
            ContainerSpawnId = containerSpawnId;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(GateId)
            && !string.IsNullOrEmpty(ContainerSpawnId);
    }

    [Serializable]
    public class ExtractionRaidReinforcementProfileDefinition
    {
        public string ProfileId;
        public float HealthMultiplier;
        public float DamageMultiplier;
        public float SpeedMultiplier;

        public ExtractionRaidReinforcementProfileDefinition()
        {
        }

        public ExtractionRaidReinforcementProfileDefinition(
            string profileId,
            float healthMultiplier,
            float damageMultiplier,
            float speedMultiplier)
        {
            ProfileId = profileId;
            HealthMultiplier = healthMultiplier;
            DamageMultiplier = damageMultiplier;
            SpeedMultiplier = speedMultiplier;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(ProfileId)
            && IsPositiveFinite(HealthMultiplier)
            && IsPositiveFinite(DamageMultiplier)
            && IsPositiveFinite(SpeedMultiplier);

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public class ExtractionRaidDifficultyRuleDefinition
    {
        public int DifficultyLevel;
        public int FirstThresholdRemainingSeconds;
        public int SecondThresholdRemainingSeconds;
        public string FirstProfileId;
        public string SecondProfileId;
        public string OvertimeEntityId;

        public ExtractionRaidDifficultyRuleDefinition()
        {
        }

        public ExtractionRaidDifficultyRuleDefinition(
            int difficultyLevel,
            int firstThresholdRemainingSeconds,
            int secondThresholdRemainingSeconds,
            string firstProfileId,
            string secondProfileId,
            string overtimeEntityId)
        {
            DifficultyLevel = difficultyLevel;
            FirstThresholdRemainingSeconds = firstThresholdRemainingSeconds;
            SecondThresholdRemainingSeconds = secondThresholdRemainingSeconds;
            FirstProfileId = firstProfileId;
            SecondProfileId = secondProfileId;
            OvertimeEntityId = overtimeEntityId;
        }

        public bool IsValid =>
            DifficultyLevel >= 0
            && FirstThresholdRemainingSeconds > SecondThresholdRemainingSeconds
            && SecondThresholdRemainingSeconds >= 0
            && !string.IsNullOrEmpty(FirstProfileId)
            && !string.IsNullOrEmpty(SecondProfileId)
            && !string.IsNullOrEmpty(OvertimeEntityId);
    }
}
