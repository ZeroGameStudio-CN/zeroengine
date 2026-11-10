using System;
using System.Collections.Generic;
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
            public int HeapIndex;
            public int NextFree;
        }

        private static long _nextOwnerId;
        private readonly long _ownerId;
        private readonly Entry[] _entries;
        private readonly Dictionary<FeedbackKey, int> _slots;
        private readonly int[] _expiryHeap;
        private int _heapCount;
        private int _firstFree;
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
            _slots = new Dictionary<FeedbackKey, int>(capacity);
            _expiryHeap = new int[capacity];
            InitializeFreeSlots();
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
            bool expired = false;
            while (_heapCount > 0 && _entries[_expiryHeap[0]].RetainUntil <= now)
            {
                RemoveEntry(_expiryHeap[0]);
                expired = true;
            }

            if (expired)
                _revision = checked(_revision + 1);

            if (_slots.TryGetValue(key, out int matchingSlot))
            {
                Entry entry = _entries[matchingSlot];
                FeedbackDecision decision = strength > entry.Strength
                    ? FeedbackDecision.Stronger
                    : FeedbackDecision.Duplicate;
                return Proposal(decision, key, strength, now, matchingSlot,
                    entry.FirstAcceptedAt, entry.Strength);
            }

            return _firstFree >= 0
                ? Proposal(FeedbackDecision.New, key, strength, now, _firstFree, now, 0)
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
                _firstFree = entry.NextFree;
                entry.Key = evaluation.Key;
                entry.FirstAcceptedAt = evaluation.FirstAcceptedAt;
                entry.RetainUntil = retainUntil;
                entry.Occupied = true;
                entry.HeapIndex = _heapCount;
                _slots.Add(entry.Key, slot);
                _expiryHeap[_heapCount++] = slot;
                _count++;
            }
            else
            {
                entry.RetainUntil = Math.Max(entry.RetainUntil, retainUntil);
            }

            entry.Strength = evaluation.Strength;
            entry.Version = checked(entry.Version + 1);
            _entries[slot] = entry;
            if (evaluation.Decision == FeedbackDecision.New)
                SiftUp(entry.HeapIndex);
            else
                SiftDown(entry.HeapIndex);
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

            RemoveEntry(handle.Slot);
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
            _slots.Clear();
            _heapCount = 0;
            InitializeFreeSlots();
            _count = 0;
            _revision = 0;
            _lastObservedTime = double.NegativeInfinity;
        }

        private void InitializeFreeSlots()
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                _entries[i].NextFree = i + 1 < _entries.Length ? i + 1 : -1;
                _entries[i].HeapIndex = -1;
            }
            _firstFree = 0;
        }

        private void RemoveEntry(int slot)
        {
            ref Entry entry = ref _entries[slot];
            _slots.Remove(entry.Key);
            int position = entry.HeapIndex;
            int lastSlot = _expiryHeap[--_heapCount];
            if (position < _heapCount)
            {
                _expiryHeap[position] = lastSlot;
                _entries[lastSlot].HeapIndex = position;
                if (position > 0 && Earlier(lastSlot, _expiryHeap[(position - 1) / 2]))
                    SiftUp(position);
                else
                    SiftDown(position);
            }
            entry.Occupied = false;
            entry.HeapIndex = -1;
            entry.NextFree = _firstFree;
            _firstFree = slot;
            _count--;
        }

        private bool Earlier(int left, int right) =>
            _entries[left].RetainUntil < _entries[right].RetainUntil;

        private void SwapHeap(int first, int second)
        {
            int a = _expiryHeap[first], b = _expiryHeap[second];
            _expiryHeap[first] = b;
            _expiryHeap[second] = a;
            _entries[a].HeapIndex = second;
            _entries[b].HeapIndex = first;
        }

        private void SiftUp(int position)
        {
            while (position > 0)
            {
                int parent = (position - 1) / 2;
                if (!Earlier(_expiryHeap[position], _expiryHeap[parent])) break;
                SwapHeap(position, parent);
                position = parent;
            }
        }

        private void SiftDown(int position)
        {
            while (true)
            {
                int left = position * 2 + 1;
                if (left >= _heapCount) return;
                int smallest = left;
                int right = left + 1;
                if (right < _heapCount && Earlier(_expiryHeap[right], _expiryHeap[left])) smallest = right;
                if (!Earlier(_expiryHeap[smallest], _expiryHeap[position])) return;
                SwapHeap(position, smallest);
                position = smallest;
            }
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
