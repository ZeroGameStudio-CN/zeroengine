using System;
using System.Collections.Generic;
using System.Threading;

namespace ZeroEngine.GameFeel
{
    /// <summary>Generation-safe ownership of one active peak contribution group.</summary>
    public readonly struct FeedbackPeakHandle : IEquatable<FeedbackPeakHandle>
    {
        internal readonly long Owner, Generation, Version;
        internal readonly int Slot;
        internal FeedbackPeakHandle(long owner, long generation, long version, int slot)
        { Owner = owner; Generation = generation; Version = version; Slot = slot; }
        public bool Equals(FeedbackPeakHandle other) => Owner == other.Owner && Generation == other.Generation && Version == other.Version && Slot == other.Slot;
        public override bool Equals(object obj) => obj is FeedbackPeakHandle other && Equals(other);
        public override int GetHashCode() => unchecked(((Owner.GetHashCode() * 397) ^ Generation.GetHashCode()) * 397 ^ Version.GetHashCode() ^ Slot);
    }

    public readonly struct FeedbackPeakRequest
    {
        public readonly FeedbackPeakHandle Group;
        public readonly bool StartsNewGroup;
        public readonly int HitId;
        public FeedbackPeakRequest(FeedbackPeakHandle group, bool startsNewGroup, int hitId)
        { Group = group; StartsNewGroup = startsNewGroup; HitId = hitId; }
    }

    /// <summary>
    /// Bounded per-target peak mixing, including nested provisional/final hit strengths.
    /// Independent hit IDs keep each base paired with its own final multiplier. The sink
    /// owns animation, seed, direction and clock, and calls End only for the exact group.
    /// A failed new output calls CancelGroup, which permits a later successful retry.
    /// </summary>
    public sealed class FeedbackPeakCoordinator
    {
        private struct Group
        {
            public bool Used;
            public FeedbackKey Key;
            public FeedbackHandle LedgerHandle;
            public long Version;
            public double Resolved;
        }

        private struct Pending
        {
            public int HitId, GroupSlot, ActiveIndex, NextFree;
            public long GroupVersion;
            public double Basis, Provisional;
        }

        private static long nextOwner;
        private readonly long owner = Interlocked.Increment(ref nextOwner);
        private long generation = 1;
        private readonly FeedbackCoordinator history;
        private readonly Group[] groups;
        private readonly Pending[] pending;
        private readonly int[] activePending;
        private readonly Dictionary<int, int> hitSlots;
        private readonly double retention;
        private int pendingCount, firstFreePending;
        private double lastNow = double.NegativeInfinity;
        public int ActiveCount { get; private set; }
        public int PendingCount => pendingCount;
        public int Capacity => history.Capacity;
        public int Count => history.Count;
        public double RetainUntil { get; private set; }

        public FeedbackPeakCoordinator(int activeCapacity = FeedbackDefaults.ModelActiveCapacity,
            int historyCapacity = FeedbackDefaults.HistoryCapacity,
            int pendingCapacity = FeedbackDefaults.ModelPendingCapacity,
            double retentionSeconds = FeedbackDefaults.GroupRetentionSeconds)
        {
            if (owner <= 0) throw new InvalidOperationException("Feedback peak identity space exhausted.");
            if (activeCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(activeCapacity));
            if (pendingCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pendingCapacity));
            if (!Finite(retentionSeconds) || retentionSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(retentionSeconds));
            history = new FeedbackCoordinator(historyCapacity);
            groups = new Group[activeCapacity];
            pending = new Pending[pendingCapacity];
            activePending = new int[pendingCapacity];
            hitSlots = new Dictionary<int, int>(pendingCapacity);
            retention = retentionSeconds;
            InitializePending();
        }

        public bool TryBegin(FeedbackKey key, double basis, double provisionalStrength, int hitId,
            double now, out FeedbackPeakRequest request)
        {
            request = default;
            if (!key.IsValid || !Finite(basis) || basis <= 0 || !Finite(provisionalStrength) || provisionalStrength < 0 ||
                hitId == 0 || !ValidTime(now) || firstFreePending < 0 || hitSlots.ContainsKey(hitId)) return false;
            lastNow = now;
            int match = -1, free = -1;
            for (int i = 0; i < groups.Length; i++)
            {
                if (!groups[i].Used) { if (free < 0) free = i; }
                else if (groups[i].Key == key) match = i;
            }
            bool startsNew = match < 0;
            if (startsNew)
            {
                if (free < 0) return false;
                var evaluation = history.Evaluate(key, 0, now);
                if (evaluation.Decision != FeedbackDecision.New) return false;
                if (history.Commit(evaluation, double.MaxValue, out var ledgerHandle) != FeedbackCommitResult.Committed) return false;
                match = free;
                groups[match].Used = true;
                groups[match].Key = key;
                groups[match].LedgerHandle = ledgerHandle;
                groups[match].Version = checked(groups[match].Version + 1);
                groups[match].Resolved = 0;
                ActiveCount++;
            }
            int slot = firstFreePending;
            firstFreePending = pending[slot].NextFree;
            pending[slot] = new Pending
            {
                HitId = hitId, GroupSlot = match, GroupVersion = groups[match].Version,
                Basis = basis, Provisional = provisionalStrength, ActiveIndex = pendingCount
            };
            activePending[pendingCount++] = slot;
            hitSlots.Add(hitId, slot);
            request = new FeedbackPeakRequest(Handle(match), startsNew, hitId);
            return true;
        }

        public bool TryResolve(int hitId, double multiplier, double now, out FeedbackPeakHandle group)
        {
            group = default;
            if (!Finite(multiplier) || multiplier < 0 || !ValidTime(now) || !hitSlots.TryGetValue(hitId, out int slot)) return false;
            var hit = pending[slot];
            double strength = hit.Basis * multiplier;
            if (!Finite(strength)) return false;
            lastNow = now;
            group = Handle(hit.GroupSlot);
            ReleasePending(slot);
            Resolve(hit.GroupSlot, hit.GroupVersion, strength, now);
            return true;
        }

        public void FinalizeUnresolved(double now)
        {
            if (!ValidTime(now)) return;
            lastNow = now;
            while (pendingCount > 0)
            {
                int slot = activePending[pendingCount - 1];
                var hit = pending[slot];
                ReleasePending(slot);
                Resolve(hit.GroupSlot, hit.GroupVersion, hit.Provisional, now);
            }
        }

        public double Strength(FeedbackPeakHandle handle)
        {
            if (!Valid(handle)) return 0;
            double strength = groups[handle.Slot].Resolved;
            for (int i = 0; i < pendingCount; i++)
            {
                var hit = pending[activePending[i]];
                if (hit.GroupSlot == handle.Slot && hit.GroupVersion == handle.Version) strength = Math.Max(strength, hit.Provisional);
            }
            return strength;
        }

        public double ResolvedStrength(FeedbackPeakHandle handle) => Valid(handle) ? groups[handle.Slot].Resolved : 0;

        public bool End(FeedbackPeakHandle handle, double now)
        {
            if (!Valid(handle) || !ValidTime(now) || !Finite(now + retention)) return false;
            lastNow = now;
            var group = groups[handle.Slot];
            double strength = Strength(handle);
            if (!history.Cancel(group.LedgerHandle)) return false;
            var completed = history.Evaluate(group.Key, strength, now);
            double retainUntil = now + retention;
            if (history.Commit(completed, retainUntil, out _) != FeedbackCommitResult.Committed) return false;
            RetainUntil = Math.Max(RetainUntil, retainUntil);
            ReleaseGroup(handle.Slot);
            return true;
        }

        public bool CancelGroup(FeedbackPeakHandle handle)
        {
            if (!Valid(handle)) return false;
            history.Cancel(groups[handle.Slot].LedgerHandle);
            ReleaseGroup(handle.Slot);
            return true;
        }

        public void Reset()
        {
            history.Reset();
            Array.Clear(groups, 0, groups.Length);
            Array.Clear(pending, 0, pending.Length);
            hitSlots.Clear();
            pendingCount = ActiveCount = 0;
            RetainUntil = 0;
            lastNow = double.NegativeInfinity;
            generation = checked(generation + 1);
            InitializePending();
        }

        private void Resolve(int slot, long version, double strength, double now)
        {
            ref var group = ref groups[slot];
            if (!group.Used || group.Version != version || strength <= group.Resolved) return;
            var evaluation = history.Evaluate(group.Key, strength, now);
            if (!evaluation.CanPlay || history.Commit(evaluation, double.MaxValue, out var handle) != FeedbackCommitResult.Committed) return;
            group.Resolved = strength;
            group.LedgerHandle = handle;
        }

        private void ReleaseGroup(int slot)
        {
            for (int i = pendingCount - 1; i >= 0; i--)
            {
                int pendingSlot = activePending[i];
                if (pending[pendingSlot].GroupSlot == slot) ReleasePending(pendingSlot);
            }
            groups[slot].Used = false;
            groups[slot].Key = default;
            groups[slot].LedgerHandle = default;
            groups[slot].Resolved = 0;
            ActiveCount--;
        }

        private void ReleasePending(int slot)
        {
            var hit = pending[slot];
            hitSlots.Remove(hit.HitId);
            int last = activePending[--pendingCount];
            if (hit.ActiveIndex < pendingCount)
            {
                activePending[hit.ActiveIndex] = last;
                pending[last].ActiveIndex = hit.ActiveIndex;
            }
            pending[slot] = new Pending { NextFree = firstFreePending };
            firstFreePending = slot;
        }

        private void InitializePending()
        {
            for (int i = 0; i < pending.Length; i++) pending[i].NextFree = i + 1 < pending.Length ? i + 1 : -1;
            firstFreePending = 0;
        }

        private FeedbackPeakHandle Handle(int slot) => new FeedbackPeakHandle(owner, generation, groups[slot].Version, slot);
        private bool Valid(FeedbackPeakHandle handle) => handle.Owner == owner && handle.Generation == generation && handle.Slot >= 0 && handle.Slot < groups.Length && groups[handle.Slot].Used && groups[handle.Slot].Version == handle.Version;
        private bool ValidTime(double now) => Finite(now) && now >= 0 && now >= lastNow;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
