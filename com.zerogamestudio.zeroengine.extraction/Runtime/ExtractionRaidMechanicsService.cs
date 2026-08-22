using System;
using System.Collections.Generic;
using System.Globalization;

namespace POB.Extraction
{
    public enum ExtractionRaidTimelineMilestone
    {
        None = 0,
        FirstDifficulty = 1,
        SecondDifficulty = 2,
        Overtime = 3
    }

    [Serializable]
    public sealed class ExtractionRaidTimelineEvaluation
    {
        public bool IsValid;
        public int RemainingSeconds;
        public int ReinforcementLevel;
        public bool FirstThresholdReached;
        public bool SecondThresholdReached;
        public bool FirstMilestoneTriggered;
        public bool SecondMilestoneTriggered;
        public bool EnteredOvertime;
        public bool IsOvertime;
    }

    public static class ExtractionRaidMechanicsService
    {
        private const string CandidateHashDomain = "zeroengine.extraction.raid-point-candidate:v1";
        private const string RandomPointHashDomain = "zeroengine.extraction.random-extraction-point:v1";

        public static bool TrySelectWeightedCandidate(
            IList<ExtractionPointDifficultyCandidate> candidates,
            int raidSeed,
            string pointId,
            out ExtractionPointDifficultyCandidate selected)
        {
            selected = null;
            if (candidates == null || candidates.Count == 0 || string.IsNullOrEmpty(pointId))
                return false;

            var valid = new List<ExtractionPointDifficultyCandidate>();
            foreach (var candidate in candidates)
                if (candidate != null && candidate.IsValid) valid.Add(candidate);
            if (valid.Count == 0) return false;

            valid.Sort(CompareCandidates);
            long totalWeight = 0;
            foreach (var candidate in valid)
            {
                totalWeight += candidate.Weight;
                if (totalWeight < 0) return false;
            }

            if (totalWeight <= 0) return false;
            uint hash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                CandidateHashDomain,
                raidSeed.ToString(CultureInfo.InvariantCulture),
                pointId));
            long target = hash % totalWeight;
            long cursor = 0;
            foreach (var candidate in valid)
            {
                cursor += candidate.Weight;
                if (target < cursor)
                {
                    selected = candidate;
                    return true;
                }
            }

            selected = valid[valid.Count - 1];
            return true;
        }

        public static bool TrySelectEnemyPointCandidate(
            ExtractionPlayableConfig config,
            string pointId,
            int difficultyLevel,
            int raidSeed,
            out ExtractionPointDifficultyCandidate selected)
        {
            selected = null;
            if (config == null) return false;
            var candidates = new List<ExtractionPointDifficultyCandidate>();
            if (!config.TryGetEnemyPointCandidates(pointId, difficultyLevel, candidates)) return false;
            return TrySelectWeightedCandidate(candidates, raidSeed, pointId, out selected);
        }

        public static bool TrySelectRandomExtractionPoint(
            IDictionary<string, int> pointIdToDistanceHops,
            int raidSeed,
            out string pointId)
        {
            pointId = null;
            if (pointIdToDistanceHops == null || pointIdToDistanceHops.Count == 0)
                return false;

            bool found = false;
            int farthest = int.MinValue;
            uint selectedHash = uint.MaxValue;
            foreach (var pair in pointIdToDistanceHops)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value < 0) continue;
                uint candidateHash = unchecked((uint)ExtractionStableHash.ComputeInt32(
                    RandomPointHashDomain,
                    raidSeed.ToString(CultureInfo.InvariantCulture),
                    pair.Key));
                if (!found
                    || pair.Value > farthest
                    || (pair.Value == farthest
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

        public static ExtractionRaidTimelineEvaluation EvaluateTimeline(
            ExtractionRaidSession session,
            long currentUnixSeconds,
            ExtractionRaidDifficultyRuleDefinition rule)
        {
            var evaluation = new ExtractionRaidTimelineEvaluation();
            if (session == null || rule == null || !rule.IsValid) return evaluation;

            session.EnsureInitialized();
            int remaining = ExtractionRaidPressureService.GetRemainingSeconds(session, currentUnixSeconds);
            evaluation.IsValid = true;
            evaluation.RemainingSeconds = remaining;
            evaluation.FirstThresholdReached = remaining <= rule.FirstThresholdRemainingSeconds;
            evaluation.SecondThresholdReached = remaining <= rule.SecondThresholdRemainingSeconds;

            if (evaluation.FirstThresholdReached)
            {
                evaluation.FirstMilestoneTriggered = session.MarkMilestoneTriggered(
                    CreateMilestoneId(rule.DifficultyLevel, ExtractionRaidTimelineMilestone.FirstDifficulty));
                if (session.Content.CurrentReinforcementLevel < 1)
                    session.Content.CurrentReinforcementLevel = 1;
            }

            if (evaluation.SecondThresholdReached)
            {
                evaluation.SecondMilestoneTriggered = session.MarkMilestoneTriggered(
                    CreateMilestoneId(rule.DifficultyLevel, ExtractionRaidTimelineMilestone.SecondDifficulty));
                if (session.Content.CurrentReinforcementLevel < 2)
                    session.Content.CurrentReinforcementLevel = 2;
            }

            if (remaining <= 0)
            {
                evaluation.EnteredOvertime = session.MarkMilestoneTriggered(
                    CreateMilestoneId(rule.DifficultyLevel, ExtractionRaidTimelineMilestone.Overtime));
                session.Content.IsOvertime = true;
            }

            evaluation.ReinforcementLevel = session.Content.CurrentReinforcementLevel;
            evaluation.IsOvertime = session.Content.IsOvertime;
            return evaluation;
        }

        public static bool TryEvaluateTimeline(
            ExtractionRaidSession session,
            long currentUnixSeconds,
            ExtractionRaidDifficultyRuleDefinition rule,
            out ExtractionRaidTimelineEvaluation evaluation)
        {
            evaluation = EvaluateTimeline(session, currentUnixSeconds, rule);
            return evaluation.IsValid;
        }

        public static bool TryMarkOvertimeEnemySpawned(ExtractionRaidSession session)
        {
            if (session == null) return false;
            session.EnsureInitialized();
            if (!session.Content.IsOvertime || session.Content.OvertimeEnemySpawned)
                return false;
            session.Content.OvertimeEnemySpawned = true;
            return true;
        }

        public static bool IsExtractionPointWindowOpen(
            ExtractionRaidSession session,
            ExtractionPointDefinition point,
            long currentUnixSeconds)
        {
            if (session == null || point == null || !point.IsValid) return false;
            long elapsed = ExtractionRaidPressureService.GetElapsedSeconds(session, currentUnixSeconds);
            int remaining = ExtractionRaidPressureService.GetRemainingSeconds(session, currentUnixSeconds);
            switch (point.Mode)
            {
                case ExtractionPointMode.Timed:
                    return remaining > 0 && remaining <= point.TimedWindowSeconds;
                case ExtractionPointMode.Boss:
                case ExtractionPointMode.Sacrifice:
                    if (!session.TryGetExtractionPointRuntimeState(point.PointId, out var state))
                        return false;
                    long windowEnd = (long)state.OpenedAtElapsedSeconds
                        + point.OpenDurationSeconds;
                    return state.OpenedAtElapsedSeconds >= 0
                        && elapsed >= state.OpenedAtElapsedSeconds
                        && elapsed < windowEnd;
                case ExtractionPointMode.Normal:
                case ExtractionPointMode.Gate:
                case ExtractionPointMode.Random:
                    return true;
                default:
                    return false;
            }
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

        public static string CreateMilestoneId(
            int difficultyLevel,
            ExtractionRaidTimelineMilestone milestone)
        {
            return "raid-timeline:"
                + difficultyLevel.ToString(CultureInfo.InvariantCulture)
                + ":"
                + ((int)milestone).ToString(CultureInfo.InvariantCulture);
        }

        private static int CompareCandidates(
            ExtractionPointDifficultyCandidate left,
            ExtractionPointDifficultyCandidate right)
        {
            int comparison = string.CompareOrdinal(left.ContentId, right.ContentId);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.PointId, right.PointId);
            if (comparison != 0) return comparison;
            comparison = left.DifficultyLevel.CompareTo(right.DifficultyLevel);
            return comparison != 0 ? comparison : left.Weight.CompareTo(right.Weight);
        }
    }

}
