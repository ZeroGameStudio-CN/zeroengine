using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    /// <summary>Opt-in references to project-owned values, using the @name argument syntax.</summary>
    public sealed class ConsoleNamedValues<T> : IDisposable where T : class
    {
        public const int MaximumNames = 256;

        private readonly object sync = new object();
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly IDisposable converter;
        private readonly Predicate<T> isAvailable;
        private Entry[] ordered;
        private bool disposed;

        public ConsoleNamedValues(ConsoleValues values, Predicate<T> isAvailable = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            this.isAvailable = isAvailable;
            converter = values.Register(typeof(T), Convert);
        }

        public IDisposable Register(string name, T value, string label = null)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64 ||
                name.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.'))
                throw new ArgumentException("Use letters, digits, hyphen, underscore or dot in a name.", nameof(name));
            if (value == null) throw new ArgumentNullException(nameof(value));
            string reference = "@" + name;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ConsoleNamedValues<T>));
                if (entries.Count >= MaximumNames || entries.ContainsKey(reference))
                    throw new InvalidOperationException("Duplicate name or reference capacity exceeded: " + reference);
                var entry = new Entry(this, reference, value, label);
                entries.Add(reference, entry);
                ordered = null;
                return entry;
            }
        }

        public Task<IReadOnlyList<ConsoleCompletionItem>> CompleteAsync(ConsoleCompletionContext context, CancellationToken cancellation)
        {
            var parameter = context.Command.Parameters[context.ParameterIndex];
            Type type = parameter.IsVariadic ? parameter.ValueType.GetElementType() : parameter.ValueType;
            if (type != typeof(T)) return Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(null);
            Entry[] snapshot;
            lock (sync)
                snapshot = ordered ??= entries.Values.OrderBy(entry => entry.Item.Value, StringComparer.Ordinal).ToArray();
            var matches = new List<ConsoleCompletionItem>(Math.Min(snapshot.Length, context.Limit));
            foreach (var entry in snapshot)
            {
                cancellation.ThrowIfCancellationRequested();
                var item = entry.Item;
                if ((item.Value.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase) ||
                     item.Label.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)) && Available(entry))
                {
                    matches.Add(item);
                    if (matches.Count == context.Limit) break;
                }
            }
            return Task.FromResult<IReadOnlyList<ConsoleCompletionItem>>(matches);
        }

        private bool Convert(ConsoleToken token, Type _, out object value, out string error)
        {
            Entry entry;
            lock (sync) entries.TryGetValue(token.Text ?? string.Empty, out entry);
            if (entry != null && Available(entry))
            {
                value = entry.Value;
                error = null;
                return true;
            }
            value = null;
            error = "Unknown or unavailable reference " + token.Text + ".";
            return false;
        }

        private bool Available(Entry entry) => entry.IsRegistered && (isAvailable == null || isAvailable(entry.Value)) && entry.IsRegistered;

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                foreach (var entry in entries.Values) entry.Detach();
                entries.Clear();
                ordered = null;
            }
            converter.Dispose();
        }

        private void Remove(Entry entry)
        {
            lock (sync)
            {
                entries.Remove(entry.Item.Value);
                ordered = null;
            }
        }

        private sealed class Entry : IDisposable
        {
            private ConsoleNamedValues<T> owner;
            public readonly T Value;
            public readonly ConsoleCompletionItem Item;
            public bool IsRegistered => Volatile.Read(ref owner) != null;

            public Entry(ConsoleNamedValues<T> owner, string reference, T value, string label)
            {
                this.owner = owner;
                Value = value;
                Item = new ConsoleCompletionItem(reference, reference, label);
            }

            public void Detach() => Interlocked.Exchange(ref owner, null);
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Remove(this);
        }
    }
}
