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

    public enum ExtractionGateMode
    {
        Capture = 0,
        Sacrifice = 1,
        Hold = 2
    }

    // Compatibility only for snapshots created by the first mechanics pilot.
    // Formal authoring distinguishes safe containers by ContainerDefinition.IsSpecial.
    public enum ExtractionLootPointKind
    {
        Normal = 0,
        Special = 1
    }

    public enum ExtractionRaidPhase
    {
        Countdown = 0,
        Overtime = 1,
        Completed = 2,
        Failed = 3
    }

    public enum ExtractionRaidEffectType
    {
        EnemyStatMultiplier = 0,
        SpawnEncounter = 1,
        SpawnContainer = 2,
        ExtendRaidDeadlineSeconds = 3,
        AddThreatLevel = 4,
        AdvanceExtractionPointSeconds = 5,
        SetRaidFlag = 6,
        UnlockBonusContainerGroup = 7
    }

    [Serializable]
    public class ExtractionRaidRuleProfileDefinition
    {
        public string ProfileId;
        public int DurationSeconds;

        public ExtractionRaidRuleProfileDefinition()
        {
        }

        public ExtractionRaidRuleProfileDefinition(string profileId, int durationSeconds)
        {
            ProfileId = profileId;
            DurationSeconds = durationSeconds;
        }

        public bool IsValid => !string.IsNullOrEmpty(ProfileId) && DurationSeconds > 0;
    }

    [Serializable]
    public class ExtractionRaidPhaseRuleDefinition
    {
        public string RuleId;
        public string ProfileId;
        public int DifficultyLevel;
        public int RemainingSeconds;
        public string EffectId;

        public ExtractionRaidPhaseRuleDefinition()
        {
        }

        public ExtractionRaidPhaseRuleDefinition(
            string ruleId,
            string profileId,
            int difficultyLevel,
            int remainingSeconds,
            string effectId)
        {
            RuleId = ruleId;
            ProfileId = profileId;
            DifficultyLevel = difficultyLevel;
            RemainingSeconds = remainingSeconds;
            EffectId = effectId;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(RuleId)
            && !string.IsNullOrEmpty(ProfileId)
            && DifficultyLevel >= 0
            && RemainingSeconds >= 0
            && !string.IsNullOrEmpty(EffectId);
    }

    [Serializable]
    public class ExtractionRaidEffectDefinition
    {
        public string EffectId;
        public ExtractionRaidEffectType EffectType;
        public string TargetId;
        public float Amount;
        public int SafetyMinimumSeconds;

        public ExtractionRaidEffectDefinition()
        {
        }

        public ExtractionRaidEffectDefinition(
            string effectId,
            ExtractionRaidEffectType effectType,
            string targetId,
            float amount,
            int safetyMinimumSeconds = 0)
        {
            EffectId = effectId;
            EffectType = effectType;
            TargetId = targetId;
            Amount = amount;
            SafetyMinimumSeconds = safetyMinimumSeconds;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(EffectId)
            && Enum.IsDefined(typeof(ExtractionRaidEffectType), EffectType)
            && !float.IsNaN(Amount)
            && !float.IsInfinity(Amount)
            && SafetyMinimumSeconds >= 0;
    }

    [Serializable]
    public class ExtractionRaidRuleSnapshot
    {
        public string ProfileId;
        public int DifficultyLevel;
        public int DurationSeconds;
        public List<ExtractionRaidPhaseRuleDefinition> PhaseRules = new();
        public List<ExtractionRaidEffectDefinition> Effects = new();

        public void EnsureInitialized()
        {
            PhaseRules ??= new List<ExtractionRaidPhaseRuleDefinition>();
            Effects ??= new List<ExtractionRaidEffectDefinition>();
        }
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
}
