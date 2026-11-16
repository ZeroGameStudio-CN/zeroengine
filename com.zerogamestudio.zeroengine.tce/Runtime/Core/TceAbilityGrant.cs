using System;
using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public enum TceGrantLifetime { Receiver = 0, Duration = 1 }

    public readonly struct TceAbilityGrantKey : IEquatable<TceAbilityGrantKey>
    {
        public readonly TceObjectRef Receiver;
        public readonly object Definition;
        public readonly string Slot;

        public TceAbilityGrantKey(TceObjectRef receiver, object definition, string slot)
        {
            Receiver = receiver;
            Definition = definition;
            Slot = slot ?? string.Empty;
        }

        public bool Equals(TceAbilityGrantKey other) => Receiver.Equals(other.Receiver) &&
            ReferenceEquals(Definition, other.Definition) && string.Equals(Slot, other.Slot, StringComparison.Ordinal);
        public override bool Equals(object other) => other is TceAbilityGrantKey key && Equals(key);
        public override int GetHashCode() => HashCode.Combine(Receiver,
            Definition == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Definition), Slot);
    }

    /// <summary>Shared ownership only. Durations and charges are supplied by the host's state/Buff system.</summary>
    public sealed class TceAbilityGrantRegistry
    {
        private sealed class Entry
        {
            public IDisposable Ability;
            public readonly HashSet<object> Sources = new(ReferenceComparer.Instance);
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }

        private readonly Dictionary<TceAbilityGrantKey, Entry> entries = new();
        public int Count => entries.Count;

        public bool TryGrant(TceAbilityGrantKey key, object source, Func<IDisposable> install, out IDisposable ability)
        {
            ability = null;
            if (!key.Receiver.IsCurrent || key.Definition == null || source == null) return false;
            if (!entries.TryGetValue(key, out var entry))
            {
                ability = install?.Invoke();
                if (ability == null) return false;
                if (!key.Receiver.IsCurrent) { ability.Dispose(); ability = null; return false; }
                entry = new Entry { Ability = ability };
                entries.Add(key, entry);
            }
            entry.Sources.Add(source);
            ability = entry.Ability;
            return true;
        }

        public void Revoke(TceAbilityGrantKey key, object source)
        {
            if (!entries.TryGetValue(key, out var entry) || !entry.Sources.Remove(source)) return;
            if (entry.Sources.Count == 0) Remove(key);
        }

        public void RevokeSource(object source)
        {
            if (source == null) return;
            foreach (var key in new List<TceAbilityGrantKey>(entries.Keys)) Revoke(key, source);
        }

        public void RemoveInactive()
        {
            if (entries.Count == 0) return;
            foreach (var key in new List<TceAbilityGrantKey>(entries.Keys))
                if (!key.Receiver.IsCurrent) Remove(key);
        }

        public void Clear()
        {
            foreach (var key in new List<TceAbilityGrantKey>(entries.Keys)) Remove(key);
        }

        private void Remove(TceAbilityGrantKey key)
        {
            if (!entries.TryGetValue(key, out var entry)) return;
            entries.Remove(key);
            entry.Ability.Dispose();
        }
    }
}
