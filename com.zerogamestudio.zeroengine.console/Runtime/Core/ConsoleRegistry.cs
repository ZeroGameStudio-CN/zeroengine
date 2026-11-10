using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ZeroEngine.Console
{
    public sealed class ConsoleBinding
    {
        public ConsoleCommand Command { get; }
        public ConsoleHandler Handler { get; }
        public ConsoleCompleter Completer { get; }
        public ConsoleBinding(ConsoleCommand command, ConsoleHandler handler, ConsoleCompleter completer = null)
        { Command = command ?? throw new ArgumentNullException(nameof(command)); Handler = handler ?? throw new ArgumentNullException(nameof(handler)); Completer = completer; }
    }

    public sealed class ConsoleRegistry : IDisposable
    {
        private readonly Dictionary<string, ConsoleEntry> ids = new Dictionary<string, ConsoleEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConsoleEntry> names = new Dictionary<string, ConsoleEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new object();
        private bool disposed;
        public long Revision { get; private set; }

        public IDisposable Register(string module, IEnumerable<ConsoleBinding> bindings)
        {
            if (string.IsNullOrWhiteSpace(module)) throw new ArgumentException("A module identity is required.", nameof(module));
            var items = (bindings ?? throw new ArgumentNullException(nameof(bindings))).ToArray();
            if (items.Length == 0) throw new ArgumentException("A registration must contain a command.", nameof(bindings));
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConsoleBinding binding in items)
            {
                if (binding == null || binding.Command.Module != module) throw new ArgumentException("Every command must belong to its registered module.", nameof(bindings));
                if (!seenIds.Add(binding.Command.Id)) throw new InvalidOperationException("Duplicate command ID: " + binding.Command.Id);
                foreach (string name in binding.Command.Aliases.Append(binding.Command.Name))
                    if (!seenNames.Add(name)) throw new InvalidOperationException("Duplicate command name: " + name);
            }
            ConsoleEntry[] entries;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ConsoleRegistry));
                foreach (string id in seenIds) if (ids.ContainsKey(id)) throw new InvalidOperationException("Duplicate command ID: " + id);
                foreach (string name in seenNames) if (names.ContainsKey(name)) throw new InvalidOperationException("Duplicate command name: " + name);
                foreach (var item in items)
                {
                    var command = item.Command;
                    if (names.ContainsKey(command.Id)) throw new InvalidOperationException("Command ID conflicts with an existing name: " + command.Id);
                    foreach (string name in command.Aliases.Prepend(command.Name))
                    {
                        if (ids.ContainsKey(name)) throw new InvalidOperationException("Command name conflicts with an existing ID: " + name);
                        if (seenIds.Contains(name) && name != command.Id) throw new InvalidOperationException("Command name conflicts with another ID: " + name);
                    }
                    if (items.Any(other => other != item && other.Command.Aliases.Prepend(other.Command.Name).Contains(command.Id, StringComparer.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Command ID conflicts with another name: " + command.Id);
                }
                entries = items.Select(binding => new ConsoleEntry(binding)).ToArray();
                foreach (ConsoleEntry entry in entries)
                {
                    ids.Add(entry.Binding.Command.Id, entry);
                    names.Add(entry.Binding.Command.Name, entry);
                    foreach (string alias in entry.Binding.Command.Aliases) names.Add(alias, entry);
                }
                Revision++;
            }
            return new ModuleLease(this, entries);
        }

        internal bool TryResolve(string value, out ConsoleEntry entry)
        {
            lock (sync)
            {
                entry = null;
                return !disposed && value != null && (names.TryGetValue(value, out entry) || ids.TryGetValue(value, out entry));
            }
        }

        internal ConsoleEntry[] Snapshot()
        {
            lock (sync) return ids.Values.OrderBy(entry => entry.Binding.Command.Id, StringComparer.Ordinal).ToArray();
        }

        private void Remove(ConsoleEntry[] entries)
        {
            lock (sync)
            {
                foreach (ConsoleEntry entry in entries)
                {
                    ConsoleCommand command = entry.Binding.Command;
                    if (!ids.TryGetValue(command.Id, out var current) || current != entry) continue;
                    ids.Remove(command.Id);
                    names.Remove(command.Name);
                    foreach (string alias in command.Aliases) names.Remove(alias);
                }
                Revision++;
            }
            StopEntries(entries);
        }

        public void Dispose()
        {
            ConsoleEntry[] entries;
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                entries = ids.Values.ToArray();
                ids.Clear(); names.Clear(); Revision++;
            }
            StopEntries(entries);
        }

        private static void StopEntries(ConsoleEntry[] entries)
        {
            List<Exception> errors = null;
            foreach (ConsoleEntry entry in entries)
            {
                try { entry.Stop(); }
                catch (Exception exception) { (errors ??= new List<Exception>()).Add(exception); }
            }
            if (errors != null) throw new AggregateException("Command cancellation callbacks failed.", errors);
        }

        private sealed class ModuleLease : IDisposable
        {
            private ConsoleRegistry owner;
            private readonly ConsoleEntry[] entries;
            public ModuleLease(ConsoleRegistry owner, ConsoleEntry[] entries) { this.owner = owner; this.entries = entries; }
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Remove(entries);
        }
    }

    internal sealed class ConsoleEntry
    {
        private readonly object sync = new object();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool active = true;
        private bool cancelling;
        private int leases;
        public ConsoleBinding Binding { get; }
        public bool IsActive { get { lock (sync) return active; } }
        public ConsoleEntry(ConsoleBinding binding) => Binding = binding;

        public bool TryAcquire(out CancellationToken token)
        {
            lock (sync)
            {
                token = default;
                if (!active) return false;
                leases++;
                token = cancellation.Token;
                return true;
            }
        }
        public void Release()
        {
            lock (sync)
            {
                leases--;
                if (!active && !cancelling && leases == 0) cancellation.Dispose();
            }
        }
        public void Stop()
        {
            lock (sync)
            {
                if (!active) return;
                active = false;
                cancelling = true;
            }
            try { cancellation.Cancel(); }
            finally
            {
                lock (sync)
                {
                    cancelling = false;
                    if (leases == 0) cancellation.Dispose();
                }
            }
        }
    }
}
