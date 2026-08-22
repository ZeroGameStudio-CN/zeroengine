using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public enum ExtractionLeverEffectType
    {
        ExtendRaidDeadlineSeconds = 0,
        AddThreatLevel = 1,
        UnlockBonusContainerGroup = 2,
        AdvanceExtractionPointSeconds = 3,
        SetRaidFlag = 4
    }

    [Serializable]
    public class ExtractionLeverEffectDefinition
    {
        public ExtractionLeverEffectType EffectType;
        public string TargetId;
        public int Amount;
        public int SafetyMinimumSeconds;

        public ExtractionLeverEffectDefinition(
            ExtractionLeverEffectType effectType,
            string targetId,
            int amount,
            int safetyMinimumSeconds = 0)
        {
            EffectType = effectType;
            TargetId = targetId;
            Amount = amount;
            SafetyMinimumSeconds = safetyMinimumSeconds;
        }
    }

    [Serializable]
    public class ExtractionLeverDefinition
    {
        public string LeverId;
        public string MapId;
        public bool SingleUse = true;
        public string DisplayName;
        public string Description;
        public List<ExtractionLeverEffectDefinition> Effects = new();

        public ExtractionLeverDefinition(string leverId, string mapId)
        {
            LeverId = leverId;
            MapId = mapId;
        }
    }

    [Serializable]
    public class ExtractionPointRuntimeState
    {
        public string PointId;
        public int EffectiveOpenAtElapsedSeconds;
        public int OpenedAtElapsedSeconds = -1;

        public ExtractionPointRuntimeState(
            string pointId,
            int effectiveOpenAtElapsedSeconds,
            int openedAtElapsedSeconds = -1)
        {
            PointId = pointId;
            EffectiveOpenAtElapsedSeconds = effectiveOpenAtElapsedSeconds;
            OpenedAtElapsedSeconds = openedAtElapsedSeconds;
        }
    }

    public enum ExtractionLeverActivationResult
    {
        Activated = 0,
        AlreadyActivated = 1,
        InvalidRequest = 2,
        MissingDefinition = 3,
        InvalidEffect = 4,
        MissingTarget = 5
    }

    public static class ExtractionLeverService
    {
        public static bool TryActivate(
            ExtractionProfileSaveData profile,
            ExtractionPlayableConfig config,
            string leverId,
            out ExtractionLeverActivationResult result)
        {
            result = ExtractionLeverActivationResult.InvalidRequest;
            var raid = profile?.ActiveRaid;
            if (!ExtractionFeatureSwitch.Enabled
                || raid == null
                || config == null
                || string.IsNullOrEmpty(leverId))
            {
                return false;
            }
            if (raid.Content.ActivatedLeverIds.Contains(leverId))
            {
                result = ExtractionLeverActivationResult.AlreadyActivated;
                return true;
            }
            if (!TryGetLever(config, raid.MapId, leverId, out var definition))
            {
                result = ExtractionLeverActivationResult.MissingDefinition;
                return false;
            }

            foreach (var effect in definition.Effects)
            {
                if (!CanApply(config, raid, effect, out result)) return false;
            }

            foreach (var effect in definition.Effects)
                Apply(config, raid, effect);

            raid.Content.ActivatedLeverIds.Add(leverId);
            string receiptId = ExtractionReceiptId.Create(
                ExtractionOperationId.Create("lever", raid.RaidId, leverId),
                "lever-activated");
            if (!raid.Content.AppliedReceiptIds.Contains(receiptId))
                raid.Content.AppliedReceiptIds.Add(receiptId);
            result = ExtractionLeverActivationResult.Activated;
            return true;
        }

        private static bool CanApply(
            ExtractionPlayableConfig config,
            ExtractionRaidSession raid,
            ExtractionLeverEffectDefinition effect,
            out ExtractionLeverActivationResult result)
        {
            result = ExtractionLeverActivationResult.InvalidEffect;
            if (effect == null || !Enum.IsDefined(typeof(ExtractionLeverEffectType), effect.EffectType))
                return false;

            switch (effect.EffectType)
            {
                case ExtractionLeverEffectType.ExtendRaidDeadlineSeconds:
                case ExtractionLeverEffectType.AddThreatLevel:
                    return effect.Amount > 0;
                case ExtractionLeverEffectType.UnlockBonusContainerGroup:
                    if (string.IsNullOrEmpty(effect.TargetId)) return false;
                    if (raid.Content.LootManifest?.Containers == null)
                    {
                        result = ExtractionLeverActivationResult.MissingTarget;
                        return false;
                    }
                    foreach (var container in raid.Content.LootManifest.Containers)
                        if (container?.BonusGroupId == effect.TargetId) return true;
                    result = ExtractionLeverActivationResult.MissingTarget;
                    return false;
                case ExtractionLeverEffectType.AdvanceExtractionPointSeconds:
                    if (effect.Amount <= 0
                        || !config.TryGetExtractionPoint(effect.TargetId, raid.MapId, out _))
                    {
                        result = ExtractionLeverActivationResult.MissingTarget;
                        return false;
                    }
                    return true;
                case ExtractionLeverEffectType.SetRaidFlag:
                    return !string.IsNullOrEmpty(effect.TargetId);
                default:
                    return false;
            }
        }

        private static void Apply(
            ExtractionPlayableConfig config,
            ExtractionRaidSession raid,
            ExtractionLeverEffectDefinition effect)
        {
            switch (effect.EffectType)
            {
                case ExtractionLeverEffectType.ExtendRaidDeadlineSeconds:
                    raid.Content.DeadlineExtensionSeconds += effect.Amount;
                    break;
                case ExtractionLeverEffectType.AddThreatLevel:
                    raid.Content.ThreatLevelDelta += effect.Amount;
                    break;
                case ExtractionLeverEffectType.UnlockBonusContainerGroup:
                    if (!raid.Content.UnlockedBonusContainerGroupIds.Contains(effect.TargetId))
                        raid.Content.UnlockedBonusContainerGroupIds.Add(effect.TargetId);
                    foreach (var container in raid.Content.LootManifest.Containers)
                        if (container?.BonusGroupId == effect.TargetId) container.Active = true;
                    break;
                case ExtractionLeverEffectType.AdvanceExtractionPointSeconds:
                    config.TryGetExtractionPoint(effect.TargetId, raid.MapId, out var point);
                    var state = GetOrCreatePointState(raid.Content, point);
                    state.EffectiveOpenAtElapsedSeconds = Math.Max(
                        effect.SafetyMinimumSeconds,
                        state.EffectiveOpenAtElapsedSeconds - effect.Amount);
                    break;
                case ExtractionLeverEffectType.SetRaidFlag:
                    raid.MarkRaidFlag(effect.TargetId);
                    break;
            }
        }

        internal static ExtractionPointRuntimeState GetOrCreatePointState(
            ExtractionActiveRaidContentState content,
            ExtractionPointDefinition point)
        {
            foreach (var state in content.ExtractionPointStates)
                if (state != null && state.PointId == point.PointId) return state;
            var created = new ExtractionPointRuntimeState(point.PointId, point.OpenAtElapsedSeconds);
            content.ExtractionPointStates.Add(created);
            return created;
        }

        private static bool TryGetLever(
            ExtractionPlayableConfig config,
            string mapId,
            string leverId,
            out ExtractionLeverDefinition definition)
        {
            foreach (var candidate in config.LeverDefinitions)
            {
                if (candidate != null && candidate.LeverId == leverId && candidate.MapId == mapId)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }
    }
}
