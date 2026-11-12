using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ZeroEngine.FogOfWar
{
    public readonly struct FogPosition
    {
        public readonly float X, Y;
        public FogPosition(float x, float y) { X = x; Y = y; }
        public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X)
            && !float.IsNaN(Y) && !float.IsInfinity(Y);
    }

    /// <summary>The caller supplies current sight; no engine, clock or scene discovery is required.</summary>
    public interface IFogVisibilityQuery
    {
        bool IsVisible(FogPosition position);
    }

    /// <summary>One logical target can own several renderers and UI elements.</summary>
    public interface IFogVisibilityTarget
    {
        bool TryGetPosition(out FogPosition position);
        void ApplyVisibility(bool visible);
        void ReleaseVisibility();
    }

    public readonly struct FogTargetHandle : IEquatable<FogTargetHandle>
    {
        internal readonly long World;
        internal readonly int Index;
        internal readonly uint Generation;
        internal FogTargetHandle(long world, int index, uint generation)
        { World = world; Index = index; Generation = generation; }
        public bool IsValid => World != 0 && Generation != 0;
        public bool Equals(FogTargetHandle other) => World == other.World
            && Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is FogTargetHandle other && Equals(other);
        public override int GetHashCode() => unchecked((World.GetHashCode() * 397 ^ Index) * 397 ^ (int)Generation);
        public static bool operator ==(FogTargetHandle left, FogTargetHandle right) => left.Equals(right);
        public static bool operator !=(FogTargetHandle left, FogTargetHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Explicit, world-scoped registration. Each Evaluate reads current positions synchronously.
    /// Callback removals are immediate and callback additions are visited later in the same call.
    /// </summary>
    public sealed class VisibilityWorld : IDisposable
    {
        private sealed class Entry
        {
            internal IFogVisibilityTarget Target;
            internal uint Generation = 1;
        }
        private sealed class ReferenceComparer : IEqualityComparer<IFogVisibilityTarget>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(IFogVisibilityTarget x, IFogVisibilityTarget y) => ReferenceEquals(x, y);
            public int GetHashCode(IFogVisibilityTarget obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private static long nextWorld;
        private readonly long identity = Interlocked.Increment(ref nextWorld);
        private readonly List<Entry> entries;
        private readonly Stack<int> free;
        private readonly List<FogTargetHandle> evaluation;
        private readonly Dictionary<IFogVisibilityTarget, FogTargetHandle> handles;
        private readonly int maximumTargets;
        private bool evaluating, disposed;
        public int Count => handles.Count;
        public ulong MembershipRevision { get; private set; }

        public VisibilityWorld(int initialCapacity = 64, int maximumTargets = 16384)
        {
            if (initialCapacity < 0 || maximumTargets < 1 || maximumTargets > int.MaxValue / 2 || initialCapacity > maximumTargets)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (identity == 0) throw new InvalidOperationException("World identity exhausted.");
            this.maximumTargets = maximumTargets;
            entries = new List<Entry>(initialCapacity);
            free = new Stack<int>(initialCapacity);
            evaluation = new List<FogTargetHandle>(initialCapacity);
            handles = new Dictionary<IFogVisibilityTarget, FogTargetHandle>(initialCapacity, ReferenceComparer.Instance);
        }

        public FogTargetHandle Register(IFogVisibilityTarget target)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VisibilityWorld));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (handles.TryGetValue(target, out var existing)) return existing;
            if (Count >= maximumTargets) throw new InvalidOperationException("Visibility target limit exceeded.");
            if (evaluating && evaluation.Count >= maximumTargets * 2)
                throw new InvalidOperationException("Visibility callbacks exceeded the synchronous registration budget.");
            int index;
            Entry entry;
            if (free.Count > 0)
            {
                index = free.Pop(); entry = entries[index];
            }
            else
            {
                index = entries.Count; entry = new Entry(); entries.Add(entry);
            }
            entry.Target = target;
            var handle = new FogTargetHandle(identity, index, entry.Generation);
            handles.Add(target, handle);
            // Generation-stamped work items allow immediate reuse of an earlier slot
            // without dropping callback additions or processing a replaced target twice.
            if (evaluating) evaluation.Add(handle);
            MembershipRevision++;
            return handle;
        }

        public bool Contains(FogTargetHandle handle) => TryGet(handle, out _);

        public bool Unregister(FogTargetHandle handle)
        {
            if (!TryGet(handle, out var entry)) return false;
            var target = entry.Target;
            entry.Target = null;
            handles.Remove(target);
            unchecked { if (++entry.Generation == 0) entry.Generation = 1; }
            free.Push(handle.Index);
            MembershipRevision++;
            target.ReleaseVisibility();
            return true;
        }

        private bool TryGet(FogTargetHandle handle, out Entry entry)
        {
            entry = null;
            if (disposed || handle.World != identity || handle.Index < 0 || handle.Index >= entries.Count) return false;
            entry = entries[handle.Index];
            return entry.Target != null && entry.Generation == handle.Generation;
        }

        public int Evaluate(IFogVisibilityQuery sight)
        {
            if (disposed) throw new ObjectDisposedException(nameof(VisibilityWorld));
            if (sight == null) throw new ArgumentNullException(nameof(sight));
            if (evaluating) throw new InvalidOperationException("Visibility evaluation cannot be nested.");
            evaluating = true;
            evaluation.Clear();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Target != null)
                    evaluation.Add(new FogTargetHandle(identity, i, entries[i].Generation));
            int applied = 0;
            try
            {
                for (int i = 0; i < evaluation.Count && !disposed; i++)
                {
                    if (!TryGet(evaluation[i], out var entry)) continue;
                    var target = entry.Target;
                    if (target == null) continue;
                    uint generation = entry.Generation;
                    bool valid = target.TryGetPosition(out var position) && position.IsFinite;
                    // A provider can remove or replace itself while supplying its position.
                    if (entry.Generation != generation || !ReferenceEquals(entry.Target, target)) continue;
                    if (valid)
                    {
                        bool visible = sight.IsVisible(position);
                        if (entry.Generation != generation || !ReferenceEquals(entry.Target, target)) continue;
                        target.ApplyVisibility(visible);
                    }
                    else target.ReleaseVisibility();
                    applied++;
                }
                return applied;
            }
            finally { evaluation.Clear(); evaluating = false; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Exception failure = null;
            foreach (var entry in entries)
            {
                var target = entry.Target; entry.Target = null;
                if (target == null) continue;
                try { target.ReleaseVisibility(); }
                catch (Exception error) { if (failure == null) failure = error; }
            }
            handles.Clear(); free.Clear(); entries.Clear(); evaluation.Clear();
            MembershipRevision++;
            if (failure != null) throw failure;
        }
    }
}
