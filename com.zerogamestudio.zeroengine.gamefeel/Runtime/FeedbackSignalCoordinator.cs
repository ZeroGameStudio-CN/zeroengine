using System;

namespace ZeroEngine.GameFeel
{
    /// <summary>Optional output adapter. Implement equality by owned output identity, not its mutable amplitude.</summary>
    public interface IFeedbackSignal
    {
        bool IsPlaying { get; }
        void Cancel();
    }

    public enum FeedbackSignalDecision
    {
        Reset, InvalidOrDisabled, GroupEnded, ExplicitOwnsGroup, Duplicate, Capacity,
        LedgerRejected, ExplicitPromotion, StrongerGroup, NewGroup, OutputUnavailable, StaleOutput
    }

    /// <summary>
    /// Reusable anchored signal ownership for camera or other continuous contributions.
    /// Distinct groups use the sink's mixing/budget; no fixed temporal cooldown is imposed.
    /// Equal/weaker copies cannot restart a signal, stronger copies preserve its phase.
    /// </summary>
    public sealed class FeedbackSignalCoordinator<TSignal>
        where TSignal : struct, IFeedbackSignal, IEquatable<TSignal>
    {
        private struct Entry
        {
            public bool Used, Fallback;
            public FeedbackKey Key;
            public FeedbackHandle Handle;
            public double RetainUntil, RequestedStrength, OutputStrength;
            public TSignal Signal;
        }

        public readonly struct Request
        {
            internal readonly FeedbackSignalCoordinator<TSignal> Owner;
            internal readonly int Slot;
            internal readonly long Version;
            internal readonly FeedbackEvaluation Evaluation;
            public readonly bool Upgrade, Promotion, Fallback;
            public readonly double Now, RequestedStrength;
            public double FirstAcceptedAt => Evaluation.FirstAcceptedAt;
            internal Request(FeedbackSignalCoordinator<TSignal> owner, int slot, long version,
                FeedbackEvaluation evaluation, bool upgrade, bool promotion, bool fallback, double now, double strength)
            {
                Owner = owner; Slot = slot; Version = version; Evaluation = evaluation;
                Upgrade = upgrade; Promotion = promotion; Fallback = fallback; Now = now; RequestedStrength = strength;
            }
        }

        private readonly FeedbackCoordinator ledger;
        private readonly Entry[] entries;
        private readonly double retention;
        private long version;
        private double lastNow = double.NegativeInfinity;
        public bool HasEntries { get; private set; }
        public FeedbackSignalDecision LastDecision { get; private set; }

        public FeedbackSignalCoordinator(int capacity = FeedbackDefaults.HistoryCapacity,
            double retentionSeconds = FeedbackDefaults.GroupRetentionSeconds)
        {
            if (!Finite(retentionSeconds) || retentionSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            ledger = new FeedbackCoordinator(capacity);
            entries = new Entry[capacity];
            retention = retentionSeconds;
        }

        public bool TryEvaluate(FeedbackKey key, double strength, bool fallback, double now, out Request request)
        {
            request = default;
            if (!key.IsValid || !Finite(now) || now < 0 || !Finite(strength) || strength <= 0)
                return Reject(FeedbackSignalDecision.InvalidOrDisabled);
            if (now < lastNow) Reset();
            lastNow = now;
            int match = -1, free = -1;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used && now >= entries[i].RetainUntil && !entries[i].Signal.IsPlaying)
                {
                    ledger.Cancel(entries[i].Handle);
                    entries[i] = default;
                    version++;
                }
                if (!entries[i].Used) { if (free < 0) free = i; }
                else if (entries[i].Key == key) match = i;
            }
            bool promotion = false;
            if (match >= 0)
            {
                var previous = entries[match];
                if (!previous.Signal.IsPlaying) return Reject(FeedbackSignalDecision.GroupEnded);
                if (!previous.Fallback && fallback) return Reject(FeedbackSignalDecision.ExplicitOwnsGroup);
                promotion = previous.Fallback && !fallback;
                if (!promotion && strength <= previous.RequestedStrength) return Reject(FeedbackSignalDecision.Duplicate);
            }
            else if (free < 0) return Reject(FeedbackSignalDecision.Capacity);

            var evaluation = ledger.Evaluate(key, strength, now);
            if (!evaluation.CanPlay && !(promotion && evaluation.Decision == FeedbackDecision.Duplicate))
                return Reject(FeedbackSignalDecision.LedgerRejected);
            request = new Request(this, match >= 0 ? match : free, version, evaluation, match >= 0,
                promotion, fallback, now, strength);
            LastDecision = promotion ? FeedbackSignalDecision.ExplicitPromotion
                : match >= 0 ? FeedbackSignalDecision.StrongerGroup : FeedbackSignalDecision.NewGroup;
            return true;
        }

        public TSignal PreviousSignal(Request request) => Valid(request) && request.Upgrade ? entries[request.Slot].Signal : default;
        public double PreviousStrength(Request request) => Valid(request) && request.Upgrade ? entries[request.Slot].OutputStrength : 0;
        public bool CanReplace(Request request) => Valid(request) && (!request.Upgrade || entries[request.Slot].Signal.IsPlaying);

        public bool Commit(Request request, TSignal signal, double strength)
        {
            if (!Valid(request) || !signal.IsPlaying || !Finite(strength) || strength <= 0)
                return Reject(FeedbackSignalDecision.OutputUnavailable);
            var previous = entries[request.Slot];
            if (request.Upgrade && !previous.Signal.IsPlaying) return false;
            var evaluation = request.Evaluation;
            if (request.Promotion)
            {
                ledger.Cancel(previous.Handle);
                evaluation = ledger.Evaluate(previous.Key, request.RequestedStrength, request.Now);
            }
            double retainUntil = Math.Max(request.Now + retention, request.Upgrade ? previous.RetainUntil : 0);
            if (ledger.Commit(evaluation, retainUntil, out var handle) != FeedbackCommitResult.Committed)
                return Reject(FeedbackSignalDecision.StaleOutput);
            entries[request.Slot] = new Entry
            {
                Used = true, Key = evaluation.Key, Handle = handle, Fallback = request.Fallback,
                RequestedStrength = request.RequestedStrength, OutputStrength = strength,
                RetainUntil = retainUntil, Signal = signal
            };
            version++;
            HasEntries = true;
            if (request.Upgrade && !previous.Signal.Equals(signal)) previous.Signal.Cancel();
            return true;
        }

        public void Reset()
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].Used) entries[i].Signal.Cancel();
                entries[i] = default;
            }
            ledger.Reset();
            version++;
            lastNow = double.NegativeInfinity;
            HasEntries = false;
            LastDecision = FeedbackSignalDecision.Reset;
        }

        private bool Valid(Request request) => ReferenceEquals(request.Owner, this) && request.Version == version && request.Slot >= 0 && request.Slot < entries.Length;
        private bool Reject(FeedbackSignalDecision reason) { LastDecision = reason; return false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
