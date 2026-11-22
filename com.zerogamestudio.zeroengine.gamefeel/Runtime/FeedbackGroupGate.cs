using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>Common identity gate. Commit only after an existing voice/device safety owner accepts output.</summary>
    public sealed class FeedbackGroupGate
    {
        private readonly FeedbackCoordinator groups;
        private readonly double retention;

        public FeedbackGroupGate(int capacity = FeedbackDefaults.HistoryCapacity,
            double retentionSeconds = FeedbackDefaults.GroupRetentionSeconds)
        {
            if (double.IsNaN(retentionSeconds) || double.IsInfinity(retentionSeconds) || retentionSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            groups = new FeedbackCoordinator(capacity);
            retention = retentionSeconds;
        }

        public FeedbackEvaluation Evaluate(FeedbackKey key, double strength, double now)
        {
            var request = groups.Evaluate(key, strength, now);
            if (request.Decision != FeedbackDecision.ClockMovedBackwards) return request;
            groups.Reset();
            return groups.Evaluate(key, strength, now);
        }

        public bool Commit(FeedbackEvaluation request, double now) =>
            groups.Commit(request, now + retention, out _) == FeedbackCommitResult.Committed;
        public void Reset() => groups.Reset();
    }

    /// <summary>Distinct contact contributions within a caller-owned batch; introduces no second merge window.</summary>
    public sealed class FeedbackBatchCounter
    {
        private readonly FeedbackCoordinator groups;
        public int Count { get; private set; }
        public FeedbackDecision LastDecision { get; private set; }

        public FeedbackBatchCounter(int capacity = FeedbackDefaults.HistoryCapacity) => groups = new FeedbackCoordinator(capacity);

        public bool Observe(FeedbackKey contact, double strength, double now, double flushAt)
        {
            if (!contact.IsValid || flushAt <= now) { Count++; return true; }
            var request = groups.Evaluate(contact, strength, now);
            LastDecision = request.Decision;
            if (!request.CanPlay || groups.Commit(request, flushAt, out _) != FeedbackCommitResult.Committed) return false;
            if (request.Decision != FeedbackDecision.New) return false;
            Count++;
            return true;
        }

        public void Reset() { groups.Reset(); Count = 0; LastDecision = default; }
    }
}
