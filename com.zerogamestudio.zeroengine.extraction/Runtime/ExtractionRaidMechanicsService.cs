using System;
using System.Collections.Generic;
using System.Globalization;

namespace POB.Extraction
{
    public static class ExtractionRaidMechanicsService
    {
        private const string EncounterHashDomain = "zeroengine.extraction.encounter-point:v2";
        private const string RandomPointHashDomain = "zeroengine.extraction.random-extraction-point:v2";

        public static bool TryCreateRuleSnapshot(
            ExtractionPlayableConfig config,
            ExtractionMapDefinition map,
            int difficultyLevel,
            out ExtractionRaidRuleSnapshot snapshot)
        {
            snapshot = null;
            if (config == null
                || map == null
                || difficultyLevel < 0
                || string.IsNullOrEmpty(map.RaidRuleProfileId)
                || !config.TryGetRaidRuleProfile(map.RaidRuleProfileId, out var profile))
            {
                return false;
            }

            var created = new ExtractionRaidRuleSnapshot
            {
                ProfileId = profile.ProfileId,
                DifficultyLevel = difficultyLevel,
                DurationSeconds = profile.DurationSeconds
            };
            var effectIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in config.RaidPhaseRules)
            {
                if (rule == null
                    || !rule.IsValid
                    || rule.ProfileId != profile.ProfileId
                    || rule.DifficultyLevel != difficultyLevel)
                {
                    continue;
                }

                if (!config.TryGetRaidEffect(rule.EffectId, out var effect)) return false;
                created.PhaseRules.Add(Clone(rule));
                if (effectIds.Add(effect.EffectId)) created.Effects.Add(Clone(effect));
            }

            created.PhaseRules.Sort(ComparePhaseRules);
            snapshot = created;
            return true;
        }

        public static bool TryGetDuePhaseRules(
            ExtractionRaidSession session,
            long currentUnixSeconds,
            List<ExtractionRaidPhaseRuleDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            session?.EnsureInitialized();
            var snapshot = session?.RuleSnapshot;
            if (snapshot == null) return false;
            snapshot.EnsureInitialized();

            int remaining = ExtractionRaidPressureService.GetRemainingSeconds(session, currentUnixSeconds);
            foreach (var rule in snapshot.PhaseRules)
            {
                if (rule == null || !rule.IsValid || remaining > rule.RemainingSeconds) continue;
                if (session.HasTriggeredMilestone(rule.RuleId)) continue;
                results.Add(rule);
            }

            results.Sort(ComparePhaseRules);
            return results.Count > 0;
        }

        public static bool TryGetSnapshotEffect(
            ExtractionRaidSession session,
            string effectId,
            out ExtractionRaidEffectDefinition effect)
        {
            effect = null;
            session?.EnsureInitialized();
            var effects = session?.RuleSnapshot?.Effects;
            if (effects == null || string.IsNullOrEmpty(effectId)) return false;
            foreach (var candidate in effects)
            {
                if (candidate != null && candidate.IsValid && candidate.EffectId == effectId)
                {
                    effect = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TryCommitPhaseRule(
            ExtractionRaidSession session,
            ExtractionRaidPhaseRuleDefinition rule)
        {
            if (session == null || rule == null || !rule.IsValid) return false;
            string receiptId = CreateEffectReceiptId(session.RaidId, rule.RuleId, rule.EffectId);
            if (session.Content.AppliedReceiptIds.Contains(receiptId)) return false;
            if (!session.MarkMilestoneTriggered(rule.RuleId)) return false;
            session.Content.AppliedReceiptIds.Add(receiptId);
            if (rule.RemainingSeconds == 0)
            {
                session.Content.Phase = ExtractionRaidPhase.Overtime;
                session.Content.IsOvertime = true;
            }
            return true;
        }

        public static bool TrySelectEncounter(
            ExtractionPlayableConfig config,
            string mapId,
            string spawnPointId,
            int difficultyLevel,
            int raidSeed,
            out ExtractionHostileExplorerDefinition selected)
        {
            selected = null;
            var candidates = new List<ExtractionHostileExplorerDefinition>();
            if (config == null
                || !config.TryGetHostileExplorerPointCandidates(
                    mapId,
                    spawnPointId,
                    difficultyLevel,
                    candidates))
            {
                return false;
            }

            candidates.Sort((left, right) => string.CompareOrdinal(left.EncounterId, right.EncounterId));
            long totalWeight = 0;
            foreach (var candidate in candidates)
                if (!candidate.IsBoss) totalWeight += candidate.Weight;
            if (totalWeight <= 0) return false;

            uint hash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                EncounterHashDomain,
                raidSeed.ToString(CultureInfo.InvariantCulture),
                mapId,
                spawnPointId,
                difficultyLevel.ToString(CultureInfo.InvariantCulture)));
            long target = hash % totalWeight;
            long cursor = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.IsBoss) continue;
                cursor += candidate.Weight;
                if (target < cursor)
                {
                    selected = candidate;
                    return true;
                }
            }

            return false;
        }

        public static bool TrySelectRandomExtractionPoint(
            IDictionary<string, float> pointIdToDistance,
            int raidSeed,
            out string pointId)
        {
            pointId = null;
            if (pointIdToDistance == null || pointIdToDistance.Count == 0) return false;

            bool found = false;
            float farthest = float.MinValue;
            uint selectedHash = uint.MaxValue;
            foreach (var pair in pointIdToDistance)
            {
                if (string.IsNullOrEmpty(pair.Key)
                    || pair.Value < 0f
                    || float.IsNaN(pair.Value)
                    || float.IsInfinity(pair.Value))
                {
                    continue;
                }

                uint candidateHash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                    RandomPointHashDomain,
                    raidSeed.ToString(CultureInfo.InvariantCulture),
                    pair.Key));
                if (!found
                    || pair.Value > farthest
                    || (Math.Abs(pair.Value - farthest) < 0.0001f
                        && (candidateHash < selectedHash
                            || (candidateHash == selectedHash
                                && string.CompareOrdinal(pair.Key, pointId) < 0))))
                {
                    found = true;
                    farthest = pair.Value;
                    selectedHash = candidateHash;
                    pointId = pair.Key;
                }
            }

            return found;
        }

        public static bool IsExtractionPointWindowOpen(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long currentUnixSeconds)
        {
            if (session == null || point == null || !point.IsValid) return false;
            long elapsed = ExtractionRaidPressureService.GetElapsedSeconds(session, currentUnixSeconds);
            switch (point.Mode)
            {
                case ExtractionPointMode.Timed:
                    return elapsed >= point.OpenAtElapsedSeconds
                        && elapsed < (long)point.OpenAtElapsedSeconds + point.EffectiveOpenDurationSeconds;
                case ExtractionPointMode.Boss:
                case ExtractionPointMode.Sacrifice:
                    if (!session.TryGetExtractionPointRuntimeState(point.PointId, out var state))
                        return false;
                    return state.OpenedAtElapsedSeconds >= 0
                        && elapsed >= state.OpenedAtElapsedSeconds
                        && elapsed < (long)state.OpenedAtElapsedSeconds + point.EffectiveOpenDurationSeconds;
                default:
                    return true;
            }
        }

        public static bool CanCompleteExtractionAtCutoff(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long channelStartedAtUnixSeconds,
            long currentUnixSeconds)
        {
            if (session == null || point == null || !point.IsValid) return false;
            if (currentUnixSeconds - channelStartedAtUnixSeconds < point.ChannelSeconds) return false;
            if (IsExtractionPointWindowOpen(session, point, currentUnixSeconds)) return true;

            long elapsed = ExtractionRaidPressureService.GetElapsedSeconds(session, currentUnixSeconds);
            long startedElapsed = ExtractionRaidPressureService.GetElapsedSeconds(
                session,
                channelStartedAtUnixSeconds);
            if (point.Mode == ExtractionPointMode.Timed)
            {
                long end = (long)point.OpenAtElapsedSeconds + point.EffectiveOpenDurationSeconds;
                return elapsed >= end
                    && startedElapsed >= point.OpenAtElapsedSeconds
                    && startedElapsed < end;
            }

            if ((point.Mode == ExtractionPointMode.Boss || point.Mode == ExtractionPointMode.Sacrifice)
                && session.TryGetExtractionPointRuntimeState(point.PointId, out var state))
            {
                long end = (long)state.OpenedAtElapsedSeconds + point.EffectiveOpenDurationSeconds;
                return elapsed >= end
                    && startedElapsed >= state.OpenedAtElapsedSeconds
                    && startedElapsed < end;
            }

            return false;
        }

        public static bool TryOpenExtractionPoint(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long currentUnixSeconds)
        {
            if (session == null || point == null || !point.IsValid) return false;
            int elapsed = (int)Math.Min(
                int.MaxValue,
                ExtractionRaidPressureService.GetElapsedSeconds(session, currentUnixSeconds));
            return session.TryMarkExtractionPointOpened(point.PointId, elapsed);
        }

        public static string CreateEffectReceiptId(string raidId, string ruleId, string effectId)
        {
            return ExtractionReceiptId.Create(
                ExtractionOperationId.Create("raid-rule", raidId, ruleId, effectId),
                "effect-applied");
        }

        private static ExtractionRaidPhaseRuleDefinition Clone(ExtractionRaidPhaseRuleDefinition source)
        {
            return new ExtractionRaidPhaseRuleDefinition(
                source.RuleId,
                source.ProfileId,
                source.DifficultyLevel,
                source.RemainingSeconds,
                source.EffectId);
        }

        private static ExtractionRaidEffectDefinition Clone(ExtractionRaidEffectDefinition source)
        {
            return new ExtractionRaidEffectDefinition(
                source.EffectId,
                source.EffectType,
                source.TargetId,
                source.Amount,
                source.SafetyMinimumSeconds);
        }

        private static int ComparePhaseRules(
            ExtractionRaidPhaseRuleDefinition left,
            ExtractionRaidPhaseRuleDefinition right)
        {
            int byRemaining = right.RemainingSeconds.CompareTo(left.RemainingSeconds);
            return byRemaining != 0 ? byRemaining : string.CompareOrdinal(left.RuleId, right.RuleId);
        }
    }
}
