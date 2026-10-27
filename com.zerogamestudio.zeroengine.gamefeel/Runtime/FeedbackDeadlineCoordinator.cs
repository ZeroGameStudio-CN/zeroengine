using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>Shared defaults. Consumers may override a channel policy without per-event cooldowns.</summary>
    public static class FeedbackDefaults
    {
        public const int HistoryCapacity = 128;
        public const double GroupRetentionSeconds = 8;
        public const double StopRecoverySeconds = .05;
        public const double StopBurstSeconds = .15;
        public const int ModelActiveCapacity = 8;
        public const int ModelPendingCapacity = 128;
    }

    public enum FeedbackDeadlineDecision
    {
        Reset, DisabledOrInvalid, GroupRejected, ExpiredGroup, WeakerInBurst,
        BurstBudget, StaleRequest, AlreadyCovered, NewBurst, StrongerGroup, StrongerHit
    }

    /// <summary>
    /// Anchored, bounded stop/hold composition. No configured minimum is added to an effect.
    /// A new ordinary burst becomes eligible after the actual output and its recovery, rather
    /// than a fixed cooldown measured from the first hit. Output ownership stays with the sink.
    /// </summary>
    public sealed class FeedbackDeadlineCoordinator
    {
        private readonly FeedbackCoordinator groups;
        private readonly double recovery, upgradeBurst, retention;
        private double burstStartedAt = double.NegativeInfinity;
        private double burstLimit;
        private double nextBurstAt = double.NegativeInfinity;
        private double endsAt = double.NegativeInfinity;
        private double strongestDuration;
        private bool containsImportant;

        public FeedbackDeadlineCoordinator(int capacity = FeedbackDefaults.HistoryCapacity,
            double recoverySeconds = FeedbackDefaults.StopRecoverySeconds,
            double upgradeBurstSeconds = FeedbackDefaults.StopBurstSeconds,
            double retentionSeconds = FeedbackDefaults.GroupRetentionSeconds)
        {
            if (!Finite(recoverySeconds) || recoverySeconds < 0) throw new ArgumentOutOfRangeException(nameof(recoverySeconds));
            if (!Finite(upgradeBurstSeconds) || upgradeBurstSeconds < 0) throw new ArgumentOutOfRangeException(nameof(upgradeBurstSeconds));
            if (!Finite(retentionSeconds) || retentionSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            groups = new FeedbackCoordinator(capacity);
            recovery = recoverySeconds;
            upgradeBurst = upgradeBurstSeconds;
            retention = retentionSeconds;
        }

        public FeedbackDeadlineDecision LastDecision { get; private set; }
        public FeedbackDecision LastGroupDecision { get; private set; }
        public double Deadline => endsAt;

        public bool TryRequest(FeedbackKey key, double duration, bool important,
            double additionalRest, double now, out double deadline)
        {
            deadline = endsAt;
            if (!key.IsValid || !Finite(now) || now < 0 || !Finite(duration) || duration <= 0 ||
                !Finite(additionalRest) || additionalRest < 0)
                return Reject(FeedbackDeadlineDecision.DisabledOrInvalid);

            var evaluation = groups.Evaluate(key, duration, now);
            if (evaluation.Decision == FeedbackDecision.ClockMovedBackwards)
            {
                Reset();
                evaluation = groups.Evaluate(key, duration, now);
            }
            LastGroupDecision = evaluation.Decision;
            if (!evaluation.CanPlay) return Reject(FeedbackDeadlineDecision.GroupRejected);

            bool sameGroup = evaluation.Decision == FeedbackDecision.Stronger;
            bool newBurst = now >= nextBurstAt;
            double firstAt = sameGroup ? evaluation.FirstAcceptedAt : now;
            if (sameGroup && (firstAt < burstStartedAt || newBurst)) return Reject(FeedbackDeadlineDecision.ExpiredGroup);
            if (!newBurst && !sameGroup && duration <= strongestDuration && (!important || containsImportant))
                return Reject(FeedbackDeadlineDecision.WeakerInBurst);

            double limit = newBurst ? now + Math.Max(duration, upgradeBurst) : burstLimit;
            if (!newBurst && now == burstStartedAt) limit = Math.Max(limit, now + duration);
            double candidateEnd = Math.Min(firstAt + duration, limit);
            if (!Finite(candidateEnd) || candidateEnd <= now) return Reject(FeedbackDeadlineDecision.BurstBudget);
            double retainUntil = Math.Max(firstAt + retention, candidateEnd + recovery);
            if (groups.Commit(evaluation, retainUntil, out _) != FeedbackCommitResult.Committed)
                return Reject(FeedbackDeadlineDecision.StaleRequest);

            if (!newBurst && candidateEnd <= endsAt)
            {
                containsImportant |= important;
                strongestDuration = Math.Max(strongestDuration, duration);
                return Reject(FeedbackDeadlineDecision.AlreadyCovered);
            }
            if (newBurst)
            {
                burstStartedAt = now;
                burstLimit = limit;
                strongestDuration = duration;
                containsImportant = important;
                endsAt = candidateEnd;
            }
            else
            {
                burstLimit = limit;
                strongestDuration = Math.Max(strongestDuration, duration);
                containsImportant |= important;
                endsAt = Math.Max(endsAt, candidateEnd);
            }
            nextBurstAt = endsAt + Math.Max(recovery, additionalRest);
            deadline = endsAt;
            LastDecision = newBurst ? FeedbackDeadlineDecision.NewBurst
                : sameGroup ? FeedbackDeadlineDecision.StrongerGroup : FeedbackDeadlineDecision.StrongerHit;
            return true;
        }

        public void Reset()
        {
            groups.Reset();
            burstStartedAt = nextBurstAt = endsAt = double.NegativeInfinity;
            burstLimit = strongestDuration = 0;
            containsImportant = false;
            LastGroupDecision = default;
            LastDecision = FeedbackDeadlineDecision.Reset;
        }

        private bool Reject(FeedbackDeadlineDecision reason) { LastDecision = reason; return false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
