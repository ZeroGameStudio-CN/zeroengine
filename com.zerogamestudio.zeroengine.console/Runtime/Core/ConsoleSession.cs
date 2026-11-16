using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public interface IConsoleDispatcher
    {
        Task<ConsoleResult> DispatchAsync(Func<Task<ConsoleResult>> work, CancellationToken cancellation);
    }

    public sealed class InlineConsoleDispatcher : IConsoleDispatcher
    {
        public Task<ConsoleResult> DispatchAsync(Func<Task<ConsoleResult>> work, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return work(); }
    }

    public sealed class ConsoleSession : IConsoleOutput, IDisposable
    {
        private readonly object sync = new object();
        private readonly Queue<ConsoleOutput> output = new Queue<ConsoleOutput>();
        private readonly Queue<string> history = new Queue<string>();
        private readonly IConsolePolicy policy;
        private readonly IConsoleDispatcher dispatcher;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private readonly int outputCapacity, historyCapacity, messageLimit;
        private CancellationTokenSource current;
        private bool disposed;
        private long outputRevision;
        private long clearRevision;
        public ConsoleRegistry Registry { get; }
        public ConsoleValues Values { get; }
        public IConsoleValueFormatter ResultFormatter { get; set; }
        public long OutputRevision { get { lock (sync) return outputRevision; } }
        public bool IsRunning { get { lock (sync) return current != null; } }

        public ConsoleSession(ConsoleRegistry registry, ConsoleValues values = null, IConsolePolicy policy = null,
            IConsoleDispatcher dispatcher = null, int outputCapacity = 256, int historyCapacity = 128, int messageLimit = 4096)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Values = values ?? new ConsoleValues();
            this.policy = policy ?? new DefaultConsolePolicy();
            this.dispatcher = dispatcher ?? new InlineConsoleDispatcher();
            if (outputCapacity < 1 || historyCapacity < 1 || messageLimit < 16) throw new ArgumentOutOfRangeException(nameof(outputCapacity));
            this.outputCapacity = outputCapacity;
            this.historyCapacity = historyCapacity;
            this.messageLimit = messageLimit;
            lifetimeToken = lifetime.Token;
        }

        public void Write(ConsoleOutput entry)
        {
            string message = entry.Message ?? string.Empty;
            if (message.Length > messageLimit)
            {
                int length = messageLimit;
                if (char.IsHighSurrogate(message[length - 1])) length--;
                message = message.Substring(0, length) + "…";
                entry = new ConsoleOutput(entry.InvocationId, entry.Level, message);
            }
            lock (sync)
            {
                if (disposed) return;
                while (output.Count >= outputCapacity) output.Dequeue();
                output.Enqueue(entry);
                outputRevision++;
            }
        }

        public ConsoleOutput[] GetOutput() { lock (sync) return output.ToArray(); }
        public ConsoleOutputSnapshot GetOutputSnapshot() { lock (sync) return new ConsoleOutputSnapshot(outputRevision, clearRevision, output.ToArray()); }
        /// <summary>Copies retained output and its revision into a caller-owned reusable buffer.</summary>
        public int CopyOutputTo(ref ConsoleOutput[] buffer, out long revision)
        {
            lock (sync)
            {
                int count = output.Count;
                if (buffer == null) buffer = Array.Empty<ConsoleOutput>();
                if (buffer.Length < count)
                {
                    int grown = buffer.Length <= int.MaxValue / 2 ? buffer.Length * 2 : count;
                    buffer = new ConsoleOutput[Math.Max(count, Math.Max(16, grown))];
                }
                output.CopyTo(buffer, 0);
                if (count < buffer.Length) Array.Clear(buffer, count, buffer.Length - count);
                revision = outputRevision;
                return count;
            }
        }
        public string[] GetHistory() { lock (sync) return history.ToArray(); }
        public void ClearOutput() { lock (sync) { output.Clear(); outputRevision++; clearRevision = outputRevision; } }

        public ConsoleCommand[] GetCommands(ConsoleCaller caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            lock (sync) if (disposed) return Array.Empty<ConsoleCommand>();
            return Registry.Snapshot().Where(entry => entry.IsActive && policy.Check(entry.Binding.Command, caller).IsSuccess)
                .Select(entry => entry.Binding.Command).ToArray();
        }

        public Task<ConsoleResult> ExecuteLineAsync(string line, ConsoleCaller caller, CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            line ??= string.Empty;
            lock (sync)
            {
                if (disposed) return Task.FromResult(ConsoleResult.Failure("session-disposed", "Console session has ended."));
                if (!string.IsNullOrWhiteSpace(line) && line.Length <= ConsoleLexer.MaximumLineLength)
                {
                    while (history.Count >= historyCapacity) history.Dequeue();
                    history.Enqueue(line);
                }
            }
            if (!ConsoleLexer.TryRead(line, out var tokens, out var error)) return Report(error);
            if (tokens.Count == 0) return Task.FromResult(ConsoleResult.Success());
            if (!Registry.TryResolve(tokens[0].Text, out var entry))
                return Report(ConsoleResult.Failure("unknown-command", "Unknown command: " + tokens[0].Text, tokens[0].Start));
            return ExecuteEntryAsync(entry, caller, tokens, null, cancellation, timeout);
        }

        public Task<ConsoleResult> ExecuteAsync(string idOrName, object[] arguments, ConsoleCaller caller,
            CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!Registry.TryResolve(idOrName, out var entry)) return Report(ConsoleResult.Failure("unknown-command", "Unknown command: " + idOrName));
            object[] captured = arguments == null ? Array.Empty<object>() : (object[])arguments.Clone();
            return ExecuteEntryAsync(entry, caller, null, captured, cancellation, timeout);
        }

        private async Task<ConsoleResult> ExecuteEntryAsync(ConsoleEntry entry, ConsoleCaller caller,
            IReadOnlyList<ConsoleToken> tokens, object[] supplied, CancellationToken cancellation, TimeSpan? timeout)
        {
            if (timeout.HasValue && (timeout.Value <= TimeSpan.Zero || timeout.Value.TotalMilliseconds > int.MaxValue))
                return await Report(ConsoleResult.Failure("invalid-deadline", "A positive bounded deadline is required.")).ConfigureAwait(false);
            ConsoleResult allowed = policy.Check(entry.Binding.Command, caller);
            if (!allowed.IsSuccess) return await Report(allowed).ConfigureAwait(false);
            if (!entry.TryAcquire(out var ownerToken))
                return await Report(ConsoleResult.Failure("module-unloaded", "Command module was unloaded.")).ConfigureAwait(false);
            CancellationTokenSource execution = null;
            CancellationTokenSource deadline = null;
            string invocationId = Guid.NewGuid().ToString("N");
            ConsoleResult result;
            try
            {
                lock (sync)
                {
                    if (disposed) return ConsoleResult.Failure("session-disposed", "Console session has ended.");
                    if (current != null)
                    {
                        var busy = ConsoleResult.Failure("busy", "Another command is still running.");
                        Write(new ConsoleOutput(invocationId, ConsoleOutputLevel.Error, busy.Message));
                        return busy;
                    }
                    if (timeout.HasValue)
                    {
                        deadline = new CancellationTokenSource();
                        deadline.CancelAfter(timeout.Value);
                        execution = cancellation.CanBeCanceled
                            ? CancellationTokenSource.CreateLinkedTokenSource(cancellation, ownerToken, deadline.Token)
                            : CancellationTokenSource.CreateLinkedTokenSource(ownerToken, deadline.Token);
                    }
                    else execution = cancellation.CanBeCanceled
                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellation, ownerToken)
                        : CancellationTokenSource.CreateLinkedTokenSource(ownerToken, CancellationToken.None);
                    current = execution;
                }
                execution.Token.ThrowIfCancellationRequested();
                ConsoleArguments arguments;
                ConsoleResult bound = tokens != null ? Values.Bind(entry.Binding.Command, tokens, 1, out arguments)
                    : Values.BindTyped(entry.Binding.Command, supplied, out arguments);
                if (!bound.IsSuccess) result = bound;
                else
                {
                    var invocation = new ConsoleInvocation(invocationId, caller, entry.Binding.Command, this);
                    result = await dispatcher.DispatchAsync(() =>
                    {
                        execution.Token.ThrowIfCancellationRequested();
                        ConsoleResult checkedPolicy = policy.Check(entry.Binding.Command, caller);
                        if (!checkedPolicy.IsSuccess) return Task.FromResult(checkedPolicy);
                        return entry.Binding.Handler(invocation, arguments, execution.Token);
                    }, execution.Token).ConfigureAwait(false);
                    if (result == null) result = ConsoleResult.Failure("missing-result", "Handler did not return a structured result.");
                    if (execution.IsCancellationRequested)
                        result = deadline != null && deadline.IsCancellationRequested ? ConsoleResult.TimedOut() : ConsoleResult.Cancelled("Cancellation was requested; completed side effects are not rolled back.");
                }
            }
            catch (OperationCanceledException)
            { result = deadline != null && deadline.IsCancellationRequested ? ConsoleResult.TimedOut() : ConsoleResult.Cancelled(); }
            catch (Exception exception)
            { result = ConsoleResult.Failure("execution-failed", exception.Message); }
            finally
            {
                lock (sync)
                {
                    if (ReferenceEquals(current, execution)) current = null;
                    execution?.Dispose();
                    deadline?.Dispose();
                }
                entry.Release();
            }
            if (!string.IsNullOrEmpty(result.Message))
                Write(new ConsoleOutput(invocationId, OutputLevel(result), result.Message));
            else if (result.Data != null)
            {
                var formatter = ResultFormatter;
                if (formatter != null)
                {
                    try
                    {
                        string text = formatter.Format(result.Data);
                        if (!string.IsNullOrEmpty(text)) Write(new ConsoleOutput(invocationId, OutputLevel(result), text));
                    }
                    catch (Exception) { Write(new ConsoleOutput(invocationId, ConsoleOutputLevel.Warning, "Result formatting failed.")); }
                }
            }
            return result;
        }

        private Task<ConsoleResult> Report(ConsoleResult result)
        {
            Write(new ConsoleOutput(string.Empty, OutputLevel(result), result.Message));
            return Task.FromResult(result);
        }

        private static ConsoleOutputLevel OutputLevel(ConsoleResult result) => result.IsSuccess ? ConsoleOutputLevel.Info :
            result.Status == ConsoleResultStatus.Unconfirmed || result.Status == ConsoleResultStatus.Started ?
                ConsoleOutputLevel.Warning : ConsoleOutputLevel.Error;

        public void CancelCurrent()
        {
            lock (sync) current?.Cancel();
        }

        public ConsoleCommandHint GetHint(string line, int caret, ConsoleCaller caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            line ??= string.Empty;
            if (caret < 0 || caret > line.Length) throw new ArgumentOutOfRangeException(nameof(caret));
            lock (sync) if (disposed) return null;
            string before = caret == line.Length ? line : line.Substring(0, caret);
            if (!ConsoleLexer.TryRead(before, out var tokens, out _, true) || tokens.Count == 0 ||
                !Registry.TryResolve(tokens[0].Text, out var entry) || !entry.IsActive || !policy.Check(entry.Binding.Command, caller).IsSuccess) return null;
            bool newToken = tokens[tokens.Count - 1].Start + tokens[tokens.Count - 1].Length < caret;
            return Hint(entry.Binding.Command, newToken ? tokens.Count : tokens.Count - 1);
        }

        private static ConsoleCommandHint Hint(ConsoleCommand command, int tokenIndex)
        {
            int parameter = tokenIndex - 1;
            if (parameter >= command.Parameters.Count)
                parameter = command.Parameters.Count > 0 && command.Parameters[command.Parameters.Count - 1].IsVariadic ? command.Parameters.Count - 1 : -1;
            return new ConsoleCommandHint(command, parameter);
        }

        public async Task<ConsoleCompletions> CompleteAsync(string line, int caret, ConsoleCaller caller,
            CancellationToken cancellation = default, int limit = 32)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            line ??= string.Empty;
            if (caret < 0 || caret > line.Length) throw new ArgumentOutOfRangeException(nameof(caret));
            limit = Math.Max(1, Math.Min(128, limit));
            var empty = new ConsoleCompletions(caret, 0, Array.Empty<ConsoleCompletionItem>(), line);
            lock (sync) if (disposed) return empty;
            cancellation.ThrowIfCancellationRequested();
            string before = caret == line.Length ? line : line.Substring(0, caret);
            if (!ConsoleLexer.TryRead(before, out var tokens, out _, true)) return empty;
            bool newToken = tokens.Count == 0 || tokens[tokens.Count - 1].Start + tokens[tokens.Count - 1].Length < caret;
            int tokenIndex = newToken ? tokens.Count : tokens.Count - 1;
            int start = newToken ? caret : tokens[tokenIndex].Start;
            string prefix = newToken ? string.Empty : tokens[tokenIndex].Text;
            ConsoleEntry resolved = null;
            if (tokens.Count > 0 && Registry.TryResolve(tokens[0].Text, out var found) && found.IsActive && policy.Check(found.Binding.Command, caller).IsSuccess)
                resolved = found;
            ConsoleCommandHint hint = resolved == null ? null : Hint(resolved.Binding.Command, tokenIndex);
            var candidates = new List<ConsoleCompletionItem>();
            if (tokenIndex == 0)
            {
                foreach (ConsoleEntry commandEntry in Registry.Snapshot())
                {
                    if (!commandEntry.IsActive || !policy.Check(commandEntry.Binding.Command, caller).IsSuccess) continue;
                    foreach (ConsoleCompletionItem candidate in commandEntry.NameCompletions)
                        if (candidate.Value.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add(candidate);
                }
            }
            else if (resolved != null && resolved.TryAcquire(out var ownerToken))
            {
                ConsoleEntry entry = resolved;
                try
                {
                    ConsoleCommand command = entry.Binding.Command;
                    int parameterIndex = tokenIndex - 1;
                    if (command.Parameters.Count == 0) return new ConsoleCompletions(caret, 0, Array.Empty<ConsoleCompletionItem>(), line, hint);
                    if (parameterIndex >= command.Parameters.Count)
                    {
                        if (!command.Parameters[command.Parameters.Count - 1].IsVariadic) return new ConsoleCompletions(caret, 0, Array.Empty<ConsoleCompletionItem>(), line, hint);
                        parameterIndex = command.Parameters.Count - 1;
                    }
                    IReadOnlyList<ConsoleCompletionItem> supplied = null;
                    if (entry.Binding.Completer != null)
                    {
                        using (var linked = cancellation.CanBeCanceled
                            ? CancellationTokenSource.CreateLinkedTokenSource(cancellation, ownerToken, lifetimeToken)
                            : CancellationTokenSource.CreateLinkedTokenSource(ownerToken, lifetimeToken))
                        {
                            var context = new ConsoleCompletionContext(command, caller, line, caret, parameterIndex, prefix, limit);
                            supplied = await entry.Binding.Completer(context, linked.Token).ConfigureAwait(false);
                            linked.Token.ThrowIfCancellationRequested();
                        }
                        if (!entry.IsActive || !policy.Check(command, caller).IsSuccess) return empty;
                    }
                    bool suppliedByProvider = supplied != null;
                    if (supplied == null) supplied = entry.DefaultCompletions[parameterIndex];
                    var suppliedIds = suppliedByProvider ? new HashSet<string>(StringComparer.Ordinal) : null;
                    foreach (var candidate in supplied)
                    {
                        if (candidate != null && (candidate.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                            candidate.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && (suppliedIds == null || suppliedIds.Add(candidate.Id)))
                        {
                            candidates.Add(candidate);
                            if (suppliedByProvider && candidates.Count == limit) break;
                        }
                    }
                }
                finally { entry.Release(); }
            }
            candidates.Sort((a, b) =>
            {
                if (tokenIndex == 0)
                {
                    int priority = (!a.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).CompareTo(
                        !b.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (priority != 0) return priority;
                }
                int value = StringComparer.Ordinal.Compare(a.Value, b.Value);
                return value != 0 ? value : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
            if (candidates.Count > limit) candidates.RemoveRange(limit, candidates.Count - limit);
            var items = candidates.ToArray();
            int end = caret;
            if (!newToken && caret < line.Length && ConsoleLexer.TryRead(line, out var completeTokens, out _, true))
                foreach (var token in completeTokens)
                    if (token.Start == start) { end = token.Start + token.Length; break; }
            return new ConsoleCompletions(start, end - start, items, line, hint);
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                try { current?.Cancel(); }
                finally
                {
                    try { lifetime.Cancel(); }
                    finally
                    {
                        lifetime.Dispose();
                        output.Clear(); history.Clear(); outputRevision++;
                    }
                }
            }
        }
    }
}
