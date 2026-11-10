using System;
using System.Collections;
using System.Collections.Generic;

namespace ZeroEngine.StatSystem.Core
{
    public enum ModifierKind { Unknown, Flat, AdditivePercent, MultiplicativePercent }

    /// <summary>Maps host-owned modifiers without changing their identity, source or formula timing.</summary>
    public interface IStatModifierAdapter<TModifier, TSource> where TModifier : class
    {
        ModifierKind Kind(TModifier modifier);
        float Value(TModifier modifier);
        int Order(TModifier modifier);
        int TypeId(TModifier modifier);
        TSource Source(TModifier modifier);
        // Matches may overlap within a (TypeId, Order) partition, never across partitions.
        bool Matches(TModifier candidate, TModifier request, TSource source);
    }

    /// <summary>Preserves object-token identity even when a token overrides Equals.</summary>
    public sealed class ReferenceSourceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceSourceComparer<T> Instance = new ReferenceSourceComparer<T>();
        private ReferenceSourceComparer() { }
        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => value == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    /// <summary>
    /// Float32 arithmetic and stable batch compaction. The host owns keys, storage,
    /// mutation notifications, formula evaluation, current values and side effects.
    /// </summary>
    public static class StatKernel<TModifier, TSource, TAdapter>
        where TModifier : class
        where TAdapter : struct, IStatModifierAdapter<TModifier, TSource>
    {
        private struct Request
        {
            public TModifier Modifier;
            public int Count;
        }

        /// <summary>Sequential operations; adjacent additive percentages form one group.</summary>
        public static float EvaluateOrdered(float baseValue, IReadOnlyList<TModifier> modifiers,
            bool ceiling, bool allowNegative, TAdapter adapter = default)
        {
            float value = baseValue, sum = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                switch (adapter.Kind(modifier))
                {
                    case ModifierKind.Flat:
                        value += adapter.Value(modifier);
                        break;
                    case ModifierKind.AdditivePercent:
                        sum += adapter.Value(modifier);
                        if (i + 1 >= modifiers.Count || adapter.Kind(modifiers[i + 1]) != ModifierKind.AdditivePercent)
                        {
                            value *= 1 + sum;
                            sum = 0;
                        }
                        break;
                    case ModifierKind.MultiplicativePercent:
                        value *= 1 + adapter.Value(modifier);
                        break;
                }
            }
            value = ceiling ? (float)Math.Ceiling(value) : value;
            if (!allowNegative && value < 0) value = 0;
            if (value > int.MaxValue) value = int.MaxValue;
            return value;
        }

        /// <summary>Legacy Data rule: (base + sum(flat)) * (1 + sum(percent)) * product(mult).</summary>
        public static float EvaluateGrouped(float baseValue, IReadOnlyList<TModifier> modifiers,
            float minimum, float maximum, TAdapter adapter = default)
        {
            float flat = 0, percent = 0, product = 1;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var modifier = modifiers[i];
                // Legacy callers evaluate every modifier, including unknown kinds.
                float value = adapter.Value(modifier);
                switch (adapter.Kind(modifier))
                {
                    case ModifierKind.Flat: flat += value; break;
                    case ModifierKind.AdditivePercent: percent += value; break;
                    case ModifierKind.MultiplicativePercent: product *= value; break;
                }
            }
            float result = baseValue;
            result += flat;
            result *= 1 + percent;
            result *= product;
            return (float)Math.Round(StatCurrentValue.Clamp(result, minimum, maximum), 4);
        }

        /// <summary>Skip only sorts whose equal-order entries are the very same reference.</summary>
        public static void Sort(List<TModifier> modifiers, Comparison<TModifier> comparison,
            TAdapter adapter = default)
        {
            for (int i = 1; i < modifiers.Count; i++)
            {
                var previous = modifiers[i - 1];
                var current = modifiers[i];
                if (adapter.Order(previous) > adapter.Order(current) ||
                    (adapter.Order(previous) == adapter.Order(current) && !ReferenceEquals(previous, current)))
                {
                    modifiers.Sort(comparison);
                    return;
                }
            }
        }

        public static bool RemoveFirst(List<TModifier> modifiers, TModifier request,
            TSource source, TAdapter adapter = default)
        {
            for (int i = 0; i < modifiers.Count; i++)
                if (adapter.Matches(modifiers[i], request, source))
                {
                    modifiers.RemoveAt(i);
                    return true;
                }
            return false;
        }

        /// <summary>
        /// Linear compaction for repeated/disjoint requests. Overlapping match sets
        /// retain the original request-by-request first-match algorithm.
        /// </summary>
        public static int RemoveBatch(List<TModifier> modifiers, IReadOnlyList<TModifier> requests,
            TSource source, out bool usedGreedyFallback, TAdapter adapter = default)
        {
            usedGreedyFallback = false;
            if (requests == null || requests.Count == 0) return 0;
            var first = requests[0];
            bool repeated = true;
            for (int i = 1; i < requests.Count; i++)
                if (!ReferenceEquals(first, requests[i])) { repeated = false; break; }
            if (repeated) return RemoveRepeated(modifiers, first, source, requests.Count, adapter);

            var partitions = new Dictionary<(int Type, int Order), Request>();
            for (int i = 0; i < requests.Count; i++)
            {
                var modifier = requests[i];
                if (modifier == null) continue;
                var key = (adapter.TypeId(modifier), adapter.Order(modifier));
                if (partitions.TryGetValue(key, out var request))
                {
                    if (!ReferenceEquals(request.Modifier, modifier))
                    {
                        usedGreedyFallback = true;
                        int count = 0;
                        for (int j = 0; j < requests.Count; j++)
                            if (RemoveFirst(modifiers, requests[j], source, adapter)) count++;
                        return count;
                    }
                    request.Count++;
                    partitions[key] = request;
                }
                else partitions.Add(key, new Request { Modifier = modifier, Count = 1 });
            }

            int write = 0, removed = 0;
            for (int read = 0; read < modifiers.Count; read++)
            {
                var candidate = modifiers[read];
                if (candidate != null &&
                    partitions.TryGetValue((adapter.TypeId(candidate), adapter.Order(candidate)), out var request) &&
                    request.Count > 0 && adapter.Matches(candidate, request.Modifier, source))
                {
                    request.Count--;
                    partitions[(adapter.TypeId(candidate), adapter.Order(candidate))] = request;
                    removed++;
                }
                else
                {
                    if (write != read) modifiers[write] = candidate;
                    write++;
                }
            }
            if (write < modifiers.Count) modifiers.RemoveRange(write, modifiers.Count - write);
            return removed;
        }

        private static int RemoveRepeated(List<TModifier> modifiers, TModifier request,
            TSource source, int count, TAdapter adapter)
        {
            int write = 0, removed = 0;
            for (int read = 0; read < modifiers.Count; read++)
            {
                var candidate = modifiers[read];
                if (removed < count && adapter.Matches(candidate, request, source)) removed++;
                else
                {
                    if (write != read) modifiers[write] = candidate;
                    write++;
                }
            }
            if (removed > 0) modifiers.RemoveRange(write, modifiers.Count - write);
            return removed;
        }

        public static int RemoveSource(List<TModifier> modifiers, TSource source,
            IEqualityComparer<TSource> comparer, TAdapter adapter = default)
        {
            int write = 0;
            for (int read = 0; read < modifiers.Count; read++)
            {
                var modifier = modifiers[read];
                if (comparer.Equals(adapter.Source(modifier), source)) continue;
                if (write != read) modifiers[write] = modifier;
                write++;
            }
            int removed = modifiers.Count - write;
            if (removed > 0) modifiers.RemoveRange(write, removed);
            return removed;
        }

        public static int RemoveSources(List<TModifier> modifiers, ISet<TSource> sources,
            TAdapter adapter = default)
        {
            if (sources == null || sources.Count == 0) return 0;
            int write = 0;
            for (int read = 0; read < modifiers.Count; read++)
            {
                var modifier = modifiers[read];
                if (sources.Contains(adapter.Source(modifier))) continue;
                if (write != read) modifiers[write] = modifier;
                write++;
            }
            int removed = modifiers.Count - write;
            if (removed > 0) modifiers.RemoveRange(write, removed);
            return removed;
        }
    }

    /// <summary>
    /// Bounded cache of actual legacy sort results. Only use with a pure, order-only
    /// adapter. Every reference and order is checked before reuse; values stay live.
    /// Not thread safe. Clear at the host's world/session boundary.
    /// </summary>
    public sealed class StatSortLayoutCache<TModifier, TSource, TAdapter>
        where TModifier : class
        where TAdapter : struct, IStatModifierAdapter<TModifier, TSource>
    {
        private sealed class Entry
        {
            public TModifier[] Input, Output;
            public int[] Orders;
        }
        private readonly List<Entry> entries = new List<Entry>();
        private readonly TAdapter adapter;
        private readonly Comparison<TModifier> comparison;
        private readonly int maximumEntries, maximumSlots, minimumCount;
        public int Count => entries.Count;
        public int RetainedModifierSlots { get; private set; }

        public StatSortLayoutCache(int maximumEntries = 16, int maximumSlots = 262144,
            int minimumCount = 128, TAdapter adapter = default)
        {
            if (maximumEntries < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
            if (maximumSlots < 0) throw new ArgumentOutOfRangeException(nameof(maximumSlots));
            if (minimumCount < 0) throw new ArgumentOutOfRangeException(nameof(minimumCount));
            this.maximumEntries = maximumEntries;
            this.maximumSlots = maximumSlots;
            this.minimumCount = minimumCount;
            this.adapter = adapter;
            comparison = (left, right) => this.adapter.Order(left).CompareTo(this.adapter.Order(right));
        }

        public void Clear()
        {
            entries.Clear();
            RetainedModifierSlots = 0;
        }

        public void Sort(List<TModifier> modifiers)
        {
            // Preserve the kernel's no-op rule, including active enumerators.
            bool needsSort = false;
            for (int i = 1; i < modifiers.Count; i++)
            {
                int previous = adapter.Order(modifiers[i - 1]), current = adapter.Order(modifiers[i]);
                if (previous > current || previous == current && !ReferenceEquals(modifiers[i - 1], modifiers[i]))
                { needsSort = true; break; }
            }
            if (!needsSort) return;
            int count = modifiers.Count;
            if (count < minimumCount || maximumEntries == 0 || count > maximumSlots / 2)
            { modifiers.Sort(comparison); return; }

            for (int e = entries.Count - 1; e >= 0; e--)
            {
                var entry = entries[e];
                if (entry.Input.Length != count) continue;
                bool matches = true;
                for (int i = 0; i < count; i++)
                    if (!ReferenceEquals(modifiers[i], entry.Input[i]) || adapter.Order(modifiers[i]) != entry.Orders[i])
                    { matches = false; break; }
                if (!matches) continue;
                modifiers.Clear();
                modifiers.AddRange(entry.Output);
                entries.RemoveAt(e);
                entries.Add(entry);
                return;
            }

            var input = modifiers.ToArray();
            var orders = new int[count];
            for (int i = 0; i < count; i++) orders[i] = adapter.Order(input[i]);
            // Learn equal-key permutations from this runtime's actual List.Sort.
            modifiers.Sort(comparison);
            while (entries.Count >= maximumEntries || RetainedModifierSlots > maximumSlots - count * 2)
            {
                RetainedModifierSlots -= entries[0].Input.Length * 2;
                entries.RemoveAt(0);
            }
            entries.Add(new Entry { Input = input, Orders = orders, Output = modifiers.ToArray() });
            RetainedModifierSlots += count * 2;
        }
    }

    /// <summary>Repeat a small template in its original order without expanding a temporary input list.</summary>
    public sealed class RepeatedModifiers<T> : IReadOnlyList<T>, ICollection<T>
    {
        private readonly IReadOnlyList<T> template;
        private readonly int originalTemplateCount;
        public int Count { get; }
        public int TemplateCount => template.Count;
        public bool IsReadOnly => true;
        public RepeatedModifiers(IReadOnlyList<T> template, int repetitions)
        {
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            if (repetitions < 0) throw new ArgumentOutOfRangeException(nameof(repetitions));
            originalTemplateCount = template.Count;
            Count = checked(originalTemplateCount * repetitions);
        }
        /// <summary>Only concrete arrays/lists allow coalescing host writes without user indexer callbacks.</summary>
        public bool TryGetStableTemplate(out IReadOnlyList<T> result)
        {
            result = template;
            return template.Count == originalTemplateCount &&
                (template.GetType() == typeof(T[]) || template.GetType() == typeof(List<T>));
        }
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (Count > array.Length - arrayIndex) throw new ArgumentException("Insufficient destination capacity.");
            if (Count == 0) return;
            if (!TryGetStableTemplate(out _))
            {
                for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i];
                return;
            }
            if (template is T[] values) Array.Copy(values, 0, array, arrayIndex, originalTemplateCount);
            else ((List<T>)template).CopyTo(0, array, arrayIndex, originalTemplateCount);
            int written = originalTemplateCount;
            while (written < Count)
            {
                int copy = Math.Min(written, Count - written);
                Array.Copy(array, arrayIndex, array, arrayIndex + written, copy);
                written += copy;
            }
        }
        public bool Contains(T item)
        {
            var comparer = EqualityComparer<T>.Default;
            foreach (var value in this) if (comparer.Equals(value, item)) return true;
            return false;
        }
        void ICollection<T>.Add(T item) => throw new NotSupportedException();
        void ICollection<T>.Clear() => throw new NotSupportedException();
        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
        public T this[int index] => index >= 0 && index < Count
            ? template[index % template.Count] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return template[i % template.Count];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
