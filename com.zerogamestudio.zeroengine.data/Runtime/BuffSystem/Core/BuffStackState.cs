using System;
using System.Collections.Generic;

namespace ZeroEngine.BuffSystem.Runtime
{
    /// <summary>Host callbacks keep gameplay side effects outside the state kernel.</summary>
    public interface IBuffStackObserver
    {
        bool IsActive { get; }
        void BeforeAddStack();
        void AfterAddStack();
        void AfterRemoveStack();
    }

    public sealed class BuffStackSnapshot
    {
        public Dictionary<string, int> SourceStacks { get; }
        public Dictionary<string, int> SourceLifetimeStacks { get; }

        public BuffStackSnapshot(IDictionary<string, int> sources, IDictionary<string, int> lifetimeSources)
        {
            SourceStacks = sources == null ? new Dictionary<string, int>() : new Dictionary<string, int>(sources);
            SourceLifetimeStacks = lifetimeSources == null ? new Dictionary<string, int>() : new Dictionary<string, int>(lifetimeSources);
            foreach (var pair in SourceLifetimeStacks)
                if (pair.Value < 0 || !SourceStacks.TryGetValue(pair.Key, out int total) || pair.Value > total)
                    throw new ArgumentException("Source lifetime layers must be a subset of their source stack count.");
        }
    }

    /// <summary>
    /// Source-aware layer state. Definitions, clocks, modifiers, ability execution,
    /// persistence formats and rendered effects belong to the consuming host.
    /// </summary>
    public sealed class BuffStackState
    {
        // Mutable maps preserve existing host APIs. Capture/Restore cross ownership boundaries by copying.
        public Dictionary<string, int> SourceStacks { get; private set; } = new Dictionary<string, int>();
        public Dictionary<string, int> SourceLifetimeStacks { get; private set; } = new Dictionary<string, int>();
        public int TotalStacks
        {
            get
            {
                int total = 0;
                foreach (int count in SourceStacks.Values) total += count;
                return total;
            }
        }

        public int AddStacks(string source, int requested, int maximum, bool sourceLifetime, IBuffStackObserver observer = null)
        {
            if (requested <= 0 || (observer != null && !observer.IsActive)) return 0;
            int actual = Math.Min(requested, Math.Max(0, maximum - TotalStacks));
            int added = 0;
            for (int i = 0; i < actual; i++)
            {
                if (observer != null && !observer.IsActive) break;
                observer?.BeforeAddStack();
                if (observer != null && !observer.IsActive) break;
                if (!SourceStacks.TryAdd(source, 1)) SourceStacks[source]++;
                if (sourceLifetime && !SourceLifetimeStacks.TryAdd(source, 1)) SourceLifetimeStacks[source]++;
                added++;
                observer?.AfterAddStack();
            }
            return added;
        }

        public int RemoveStacks(string source, int requested, IBuffStackObserver observer = null)
        {
            if (requested <= 0 || TotalStacks <= 0) return 0;
            int removed = 0;
            for (int i = 0; i < requested; i++)
            {
                string key = source;
                if (key == null)
                    foreach (var entry in SourceStacks) { key = entry.Key; break; }
                if (key == null || !SourceStacks.ContainsKey(key)) break;
                SourceStacks[key]--;
                // Consume scoped layers first and update the subset before reentrant callbacks.
                if (SourceLifetimeStacks.TryGetValue(key, out int scoped))
                {
                    if (scoped <= 1) SourceLifetimeStacks.Remove(key);
                    else SourceLifetimeStacks[key] = scoped - 1;
                }
                if (SourceStacks[key] <= 0) SourceStacks.Remove(key);
                observer?.AfterRemoveStack();
                removed++;
            }
            return removed;
        }

        /// <summary>Aggregate adapters retain their existing arithmetic, including signed legacy inputs.</summary>
        public void SetSourceStackCount(string source, int count)
        {
            if (count == 0) SourceStacks.Remove(source);
            else SourceStacks[source] = count;
        }

        public BuffStackSnapshot Capture() => new BuffStackSnapshot(SourceStacks, SourceLifetimeStacks);

        public void Restore(IDictionary<string, int> sources, IDictionary<string, int> lifetimeSources)
        {
            var snapshot = new BuffStackSnapshot(sources, lifetimeSources);
            SourceStacks = snapshot.SourceStacks;
            SourceLifetimeStacks = snapshot.SourceLifetimeStacks;
        }

        public void Clear()
        {
            SourceStacks.Clear();
            SourceLifetimeStacks.Clear();
        }
    }
}
