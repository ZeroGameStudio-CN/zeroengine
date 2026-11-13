using System;
using System.Collections.Generic;

namespace ZeroEngine.Multiplayer.Replication
{
    public enum ReplicatedLeaseOperation : byte { Emit = 1, Begin = 2, Change = 3, End = 4 }

    public readonly struct ReplicatedLeaseUpdate
    {
        public ReplicatedLeaseUpdate(uint epoch, uint instance, uint revision, ReplicatedLeaseOperation operation)
        { Epoch = epoch; Instance = instance; Revision = revision; Operation = operation; }
        public uint Epoch { get; }
        public uint Instance { get; }
        public uint Revision { get; }
        public ReplicatedLeaseOperation Operation { get; }
        public bool IsValid => Epoch != 0 && Instance != 0 && Revision != 0
            && Operation >= ReplicatedLeaseOperation.Emit && Operation <= ReplicatedLeaseOperation.End;
    }

    // Owns only ordering/lifetime. Payload, transport, resource and gameplay validation stay with adapters.
    public sealed class ReplicatedLeaseLedger
    {
        private readonly int maximumActive, maximumTombstones;
        private readonly Dictionary<uint, uint> revisions = new Dictionary<uint, uint>();
        private readonly HashSet<uint> ended = new HashSet<uint>();
        private readonly Queue<uint> retirement = new Queue<uint>();
        private uint epoch, retiredThrough;
        public ReplicatedLeaseLedger(int maximumActive, int maximumTombstones)
        {
            if (maximumActive <= 0) throw new ArgumentOutOfRangeException(nameof(maximumActive));
            if (maximumTombstones <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTombstones));
            this.maximumActive = maximumActive; this.maximumTombstones = maximumTombstones;
        }
        public int ActiveCount => revisions.Count;
        public int TombstoneCount => ended.Count;
        public void Reset(uint nextEpoch)
        { epoch = nextEpoch; retiredThrough = 0; revisions.Clear(); ended.Clear(); retirement.Clear(); }
        public bool AcceptBaseline(ReplicatedLeaseUpdate value)
        {
            if (!value.IsValid || value.Operation != ReplicatedLeaseOperation.Begin) return false;
            if (value.Epoch == epoch && !ended.Contains(value.Instance)
                && revisions.TryGetValue(value.Instance, out uint revision) && revision == value.Revision) return true;
            return Accept(value);
        }
        public bool Accept(ReplicatedLeaseUpdate value)
        {
            if (!value.IsValid || value.Epoch != epoch || ended.Contains(value.Instance)) return false;
            bool active = revisions.TryGetValue(value.Instance, out uint revision);
            if (active && !MessageSequenceGate.IsNewer(value.Revision, revision)) return false;
            if (!active && retiredThrough != 0 && !MessageSequenceGate.IsNewer(value.Instance, retiredThrough)) return false;
            if (value.Operation == ReplicatedLeaseOperation.End)
            {
                revisions.Remove(value.Instance); ended.Add(value.Instance); retirement.Enqueue(value.Instance);
                if (retirement.Count > maximumTombstones)
                {
                    uint retired = retirement.Dequeue(); ended.Remove(retired);
                    if (retiredThrough == 0 || MessageSequenceGate.IsNewer(retired, retiredThrough)) retiredThrough = retired;
                }
            }
            else
            {
                if (!active && revisions.Count >= maximumActive) return false;
                revisions[value.Instance] = value.Revision;
            }
            return true;
        }
    }
}
