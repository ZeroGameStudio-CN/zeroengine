using System;
using System.Collections.Generic;

namespace ZeroEngine.GameFeel
{
    /// <summary>One representative for equivalent live visual outputs. The caller owns
    /// spatial/style keys, eligibility, renderer changes and pooled-lifetime release.</summary>
    public sealed class VisualEffectCoalescer<TKey, TOutput> where TKey : struct, IEquatable<TKey> where TOutput : class
    {
        private struct Group { internal int First, Last, LastRepresentative, Representatives; }
        private struct Slot
        {
            internal TKey Key;
            internal TOutput Output;
            internal long Token;
            internal int Previous, Next;
            internal bool Representative;
        }
        private readonly long owner = VisualEffectLease.CreateOwner();
        private readonly Slot[] slots;
        private readonly Dictionary<TKey, Group> groups;
        private readonly Dictionary<TKey, Group>.KeyCollection keys;
        private readonly TKey[] keyScratch;
        private long generation = 1, nextToken;
        private int free;
        public int Count { get; private set; }
        public int GroupCount => groups.Count;
        public int RepresentativeCount { get; private set; }
        public int MaximumRepresentatives { get; private set; }

        public VisualEffectCoalescer(int capacity = 512, int maximumRepresentatives = 1)
        {
            if (capacity < 1 || maximumRepresentatives < 1 || maximumRepresentatives > capacity) throw new ArgumentOutOfRangeException(nameof(capacity));
            slots = new Slot[capacity]; groups = new Dictionary<TKey, Group>(capacity);
            keys = groups.Keys; keyScratch = new TKey[capacity]; MaximumRepresentatives = maximumRepresentatives;
            Reset();
        }

        // Capacity exhaustion preserves the output. It cannot erase the last cue.
        public bool TryAcquire(TKey key, TOutput output, out VisualEffectLease lease, out bool representative)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            lease = default; representative = true;
            if (free < 0 || nextToken == long.MaxValue) return false;
            int index = free; free = slots[index].Next;
            bool existing = groups.TryGetValue(key, out var group);
            long token = ++nextToken;
            representative = !existing || group.Representatives < MaximumRepresentatives;
            slots[index] = new Slot { Key = key, Output = output, Token = token, Previous = existing ? group.Last : -1, Next = -1, Representative = representative };
            if (existing)
            {
                slots[group.Last].Next = index; group.Last = index;
            }
            else group = new Group { First = index, Last = index, LastRepresentative = -1 };
            if (representative) { group.Representatives++; group.LastRepresentative = index; RepresentativeCount++; }
            groups[key] = group; Count++;
            lease = new VisualEffectLease(owner, generation, token, index);
            return true;
        }

        public bool IsRepresentative(VisualEffectLease lease) => Valid(lease) && slots[lease.Slot].Representative;

        public bool ReplaceOutput(VisualEffectLease lease, TOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (!Valid(lease)) return false;
            slots[lease.Slot].Output = output; return true;
        }

        // Leases stay valid. The caller reapplies its outputs after a changed limit.
        public bool ConfigureMaximum(int maximum)
        {
            if (maximum < 1 || maximum > slots.Length) throw new ArgumentOutOfRangeException(nameof(maximum));
            if (maximum == MaximumRepresentatives) return false;
            MaximumRepresentatives = maximum; RepresentativeCount = 0;
            int count = groups.Count; keys.CopyTo(keyScratch, 0);
            for (int i = 0; i < count; i++)
            {
                var key = keyScratch[i]; var group = groups[key]; group.Representatives = 0; group.LastRepresentative = -1;
                for (int node = group.First; node >= 0; node = slots[node].Next)
                {
                    bool selected = group.Representatives < maximum; slots[node].Representative = selected;
                    if (selected) { group.Representatives++; group.LastRepresentative = node; RepresentativeCount++; }
                }
                groups[key] = group; keyScratch[i] = default;
            }
            return true;
        }

        public bool Release(VisualEffectLease lease, out TOutput promoted)
        {
            promoted = null;
            if (!Valid(lease)) return false;
            int index = lease.Slot; var slot = slots[index]; var group = groups[slot.Key];
            if (slot.Previous >= 0) slots[slot.Previous].Next = slot.Next;
            else
            {
                group.First = slot.Next;
            }
            if (slot.Next >= 0) slots[slot.Next].Previous = slot.Previous;
            else group.Last = slot.Previous;
            if (slot.Representative)
            {
                group.Representatives--; RepresentativeCount--;
                if (group.LastRepresentative == index) group.LastRepresentative = slot.Previous;
                int next = group.LastRepresentative >= 0 ? slots[group.LastRepresentative].Next : group.First;
                if (next >= 0)
                {
                    slots[next].Representative = true; group.LastRepresentative = next;
                    group.Representatives++; RepresentativeCount++; promoted = slots[next].Output;
                }
            }
            if (group.First < 0) groups.Remove(slot.Key); else groups[slot.Key] = group;
            slots[index] = new Slot { Previous = -1, Next = free }; free = index; Count--;
            return true;
        }

        public void Reset()
        {
            generation = generation == long.MaxValue ? 1 : generation + 1;
            groups.Clear(); Array.Clear(slots, 0, slots.Length); Array.Clear(keyScratch, 0, keyScratch.Length);
            Count = RepresentativeCount = 0; free = 0; nextToken = 0;
            for (int i = 0; i < slots.Length; i++) slots[i].Next = i + 1 < slots.Length ? i + 1 : -1;
        }

        private bool Valid(VisualEffectLease lease) => lease.Owner == owner && lease.Generation == generation &&
            lease.Slot >= 0 && lease.Slot < slots.Length && lease.Token != 0 && slots[lease.Slot].Token == lease.Token;
    }
}
