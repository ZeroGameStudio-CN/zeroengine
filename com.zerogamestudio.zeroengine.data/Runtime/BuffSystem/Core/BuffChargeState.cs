using System;
using System.Collections.Generic;

namespace ZeroEngine.BuffSystem.Runtime
{
    /// <summary>A granted charge keeps its identity after the last active Buff layer is consumed.</summary>
    public sealed class BuffChargeSnapshot
    {
        public bool Initialized { get; }
        public BuffStackSnapshot Stacks { get; }

        public BuffChargeSnapshot(bool initialized, IDictionary<string, int> sources, IDictionary<string, int> lifetimeSources = null)
        {
            Stacks = new BuffStackSnapshot(sources, lifetimeSources);
            foreach (int count in Stacks.SourceStacks.Values)
                if (count < 0) throw new ArgumentException("Charge counts cannot be negative.");
            if (!initialized && Stacks.SourceStacks.Count != 0)
                throw new ArgumentException("An unissued charge cannot have remaining layers.");
            Initialized = initialized;
        }
    }

    /// <summary>
    /// Run/owner identity and storage belong to the host. The active Buff borrows Stacks;
    /// no second used-count is maintained, and detaching that view must not clear this state.
    /// </summary>
    public sealed class BuffChargeState
    {
        public bool Initialized { get; private set; }
        public BuffStackState Stacks { get; } = new BuffStackState();

        public bool BeginGrant()
        {
            if (Initialized) return false;
            Initialized = true; // Publish before callbacks can synchronously attempt another grant.
            return true;
        }

        public int GrantOnce(string source, int count, int maximum, IBuffStackObserver observer = null)
        {
            if (string.IsNullOrEmpty(source)) throw new ArgumentException("A stable grant source is required.", nameof(source));
            if (count < 1 || maximum < 1) throw new ArgumentOutOfRangeException(nameof(count));
            if (!BeginGrant()) return 0;
            return Stacks.AddStacks(source, count, maximum, false, observer);
        }

        public BuffChargeSnapshot Capture() => new BuffChargeSnapshot(Initialized, Stacks.SourceStacks, Stacks.SourceLifetimeStacks);

        public void Restore(BuffChargeSnapshot state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            // Validate a complete copy before replacing any live state.
            var copy = new BuffChargeSnapshot(state.Initialized, state.Stacks.SourceStacks, state.Stacks.SourceLifetimeStacks);
            Stacks.Restore(copy.Stacks.SourceStacks, copy.Stacks.SourceLifetimeStacks);
            Initialized = copy.Initialized;
        }

        public void Reset()
        {
            Stacks.Clear();
            Initialized = false;
        }
    }
}
