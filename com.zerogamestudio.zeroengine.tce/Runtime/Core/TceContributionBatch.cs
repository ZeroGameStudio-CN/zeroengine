using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    /// <summary>One event's competing contributions. The domain adapter supplies a deterministic strength.</summary>
    public sealed class TceContributionBatch<TKey, TValue>
    {
        private readonly Dictionary<TKey, (float Strength, TValue Value)> entries;
        private bool closed;
        public TceContributionBatch(IEqualityComparer<TKey> comparer = null) =>
            entries = new Dictionary<TKey, (float, TValue)>(comparer);
        public bool Offer(TKey key, float strength, TValue value)
        {
            if (closed || !float.IsFinite(strength)) return false;
            if (entries.TryGetValue(key, out var current) && current.Strength >= strength) return false;
            entries[key] = (strength, value);
            return true;
        }
        public void Complete(Action<TValue> apply)
        {
            if (closed) return;
            closed = true;
            var snapshot = new List<TValue>(entries.Count);
            foreach (var entry in entries.Values) snapshot.Add(entry.Value);
            entries.Clear();
            foreach (var value in snapshot) apply(value);
        }
        public void Cancel() { closed = true; entries.Clear(); }
    }
}
