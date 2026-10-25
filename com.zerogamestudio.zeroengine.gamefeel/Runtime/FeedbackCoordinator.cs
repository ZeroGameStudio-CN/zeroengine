using System;
using System.Threading;

namespace ZeroEngine.GameFeel
{
    /// <summary>
    /// Fixed-capacity identity ledger for a single consumer-owned channel and scope.
    /// Strength is only an ordering supplied by that consumer; no playback units, cooldown,
    /// budget, queuing, Unity objects, or device behavior are imposed here.
    /// Not thread safe. Evaluate, attempt output, and Commit synchronously without reentry.
    /// </summary>
    public sealed class FeedbackCoordinator
    {
        private struct Entry
        {
            public bool Occupied;
            public FeedbackKey Key;
            public double FirstAcceptedAt;
            public double Strength;
            public double RetainUntil;
            public long Version;
        }

        private static long _nextOwnerId;
        private readonly long _ownerId;
        private readonly Entry[] _entries;
        private long _generation = 1;
        private long _revision;
        private double _lastObservedTime = double.NegativeInfinity;
        private int _count;

        public int Capacity => _entries.Length;

        /// <summary>Retained entries after the most recent valid Evaluate or mutation.</summary>
        public int Count => _count;

        public FeedbackCoordinator(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _ownerId = Interlocked.Increment(ref _nextOwnerId);
            if (_ownerId <= 0)
                throw new InvalidOperationException("Feedback coordinator identity space exhausted.");
            _entries = new Entry[capacity];
        }

        /// <summary>
        /// Expires entries at retainUntil &lt;= now, then compares this key with its last
        /// committed strength. Equal or weaker requests do not refresh retention.
        /// Invalid requests and backwards clocks leave all state unchanged.
        /// </summary>
        public FeedbackEvaluation Evaluate(FeedbackKey key, double strength, double now)
        {
            if (!key.IsValid)
                return Reject(FeedbackDecision.InvalidKey, key, strength, now);
            if (!IsFinite(strength) || strength < 0)
                return Reject(FeedbackDecision.InvalidStrength, key, strength, now);
            if (!IsFinite(now) || now < 0)
                return Reject(FeedbackDecision.InvalidTime, key, strength, now);
            if (now < _lastObservedTime)
                return Reject(FeedbackDecision.ClockMovedBackwards, key, strength, now);

            _lastObservedTime = now;
            int freeSlot = -1;
            int matchingSlot = -1;
            bool expired = false;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Occupied && _entries[i].RetainUntil <= now)
                {
                    _entries[i].Occupied = false;
                    _count--;
                    expired = true;
                }

                if (!_entries[i].Occupied)
                {
                    if (freeSlot < 0)
                        freeSlot = i;
                }
                else if (_entries[i].Key == key)
                {
                    matchingSlot = i;
                }
            }

            if (expired)
                _revision = checked(_revision + 1);

            if (matchingSlot >= 0)
            {
                Entry entry = _entries[matchingSlot];
                FeedbackDecision decision = strength > entry.Strength
                    ? FeedbackDecision.Stronger
                    : FeedbackDecision.Duplicate;
                return Proposal(decision, key, strength, now, matchingSlot,
                    entry.FirstAcceptedAt, entry.Strength);
            }

            return freeSlot >= 0
                ? Proposal(FeedbackDecision.New, key, strength, now, freeSlot, now, 0)
                : Reject(FeedbackDecision.CapacityExceeded, key, strength, now);
        }

        /// <summary>
        /// Records a proposal only after the consumer accepts its output. Retention must be
        /// finite and strictly later than now; it is a deduplication deadline, not an effect
        /// duration. Stronger commits preserve the first event time and cannot shorten the
        /// previously committed retention. Any intervening mutation or clock advance makes
        /// the proposal stale, including a second attempt to commit the same proposal.
        /// </summary>
        public FeedbackCommitResult Commit(FeedbackEvaluation evaluation, double retainUntil,
            out FeedbackHandle handle)
        {
            handle = default;
            if (!evaluation.CanPlay)
                return FeedbackCommitResult.NotAccepted;
            if (evaluation.OwnerId != _ownerId)
                return FeedbackCommitResult.WrongCoordinator;
            if (evaluation.Generation != _generation || evaluation.Revision != _revision ||
                evaluation.EvaluatedAt != _lastObservedTime)
                return FeedbackCommitResult.StaleEvaluation;
            if (!IsFinite(retainUntil) || retainUntil <= _lastObservedTime)
                return FeedbackCommitResult.InvalidRetention;

            int slot = evaluation.Slot;
            Entry entry = _entries[slot];
            if (evaluation.Decision == FeedbackDecision.New)
            {
                entry.Key = evaluation.Key;
                entry.FirstAcceptedAt = evaluation.FirstAcceptedAt;
                entry.RetainUntil = retainUntil;
                entry.Occupied = true;
                _count++;
            }
            else
            {
                entry.RetainUntil = Math.Max(entry.RetainUntil, retainUntil);
            }

            entry.Strength = evaluation.Strength;
            entry.Version = checked(entry.Version + 1);
            _entries[slot] = entry;
            _revision = checked(_revision + 1);
            handle = new FeedbackHandle(_ownerId, _generation, entry.Version, slot);
            return FeedbackCommitResult.Committed;
        }

        /// <summary>
        /// Releases only the exact committed version owned by this handle. Use when its
        /// scope is cancelled; normal effect completion need not end the deduplication window.
        /// </summary>
        public bool Cancel(FeedbackHandle handle)
        {
            if (handle.OwnerId != _ownerId || handle.Generation != _generation ||
                handle.Slot < 0 || handle.Slot >= _entries.Length)
                return false;

            Entry entry = _entries[handle.Slot];
            if (!entry.Occupied || entry.Version != handle.EntryVersion)
                return false;

            _entries[handle.Slot].Occupied = false;
            _count--;
            _revision = checked(_revision + 1);
            return true;
        }

        /// <summary>
        /// Starts a new scope lifecycle and invalidates every old evaluation and handle.
        /// A new lifecycle may use a new clock origin. Actual effects remain consumer-owned.
        /// </summary>
        public void Reset()
        {
            _generation = checked(_generation + 1);
            Array.Clear(_entries, 0, _entries.Length);
            _count = 0;
            _revision = 0;
            _lastObservedTime = double.NegativeInfinity;
        }

        private FeedbackEvaluation Proposal(FeedbackDecision decision, FeedbackKey key,
            double strength, double now, int slot, double firstAcceptedAt, double previousStrength)
        {
            return new FeedbackEvaluation(decision, key, strength, firstAcceptedAt, previousStrength,
                _ownerId, _generation, _revision, slot, now);
        }

        private FeedbackEvaluation Reject(FeedbackDecision decision, FeedbackKey key,
            double strength, double now)
        {
            return Proposal(decision, key, strength, now, -1, 0, 0);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
