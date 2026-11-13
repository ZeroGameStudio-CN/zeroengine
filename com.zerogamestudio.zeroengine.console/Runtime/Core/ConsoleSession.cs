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
            return ExecuteEntryAsync(entry, caller, () =>
            {
                var args = tokens.Skip(1).ToArray();
                ConsoleResult bound = Values.Bind(entry.Binding.Command, args, out var values);
                return (bound, values);
            }, cancellation, timeout);
        }

        public Task<ConsoleResult> ExecuteAsync(string idOrName, object[] arguments, ConsoleCaller caller,
            CancellationToken cancellation = default, TimeSpan? timeout = null)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!Registry.TryResolve(idOrName, out var entry)) return Report(ConsoleResult.Failure("unknown-command", "Unknown command: " + idOrName));
            object[] captured = arguments == null ? Array.Empty<object>() : (object[])arguments.Clone();
            return ExecuteEntryAsync(entry, caller, () =>
            {
                ConsoleResult bound = Values.BindTyped(entry.Binding.Command, captured, out var values);
                return (bound, values);
            }, cancellation, timeout);
        }

        private async Task<ConsoleResult> ExecuteEntryAsync(ConsoleEntry entry, ConsoleCaller caller,
            Func<(ConsoleResult Result, ConsoleArguments Arguments)> bind, CancellationToken cancellation, TimeSpan? timeout)
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
                    deadline = new CancellationTokenSource();
                    if (timeout.HasValue) deadline.CancelAfter(timeout.Value);
                    execution = CancellationTokenSource.CreateLinkedTokenSource(cancellation, ownerToken, lifetimeToken, deadline.Token);
                    current = execution;
                }
                execution.Token.ThrowIfCancellationRequested();
                var bound = bind();
                if (!bound.Result.IsSuccess) result = bound.Result;
                else
                {
                    var invocation = new ConsoleInvocation(invocationId, caller, entry.Binding.Command, this);
                    result = await dispatcher.DispatchAsync(async () =>
                    {
                        execution.Token.ThrowIfCancellationRequested();
                        ConsoleResult checkedPolicy = policy.Check(entry.Binding.Command, caller);
                        if (!checkedPolicy.IsSuccess) return checkedPolicy;
                        return await entry.Binding.Handler(invocation, bound.Arguments, execution.Token).ConfigureAwait(false);
                    }, execution.Token).ConfigureAwait(false);
                    if (result == null) result = ConsoleResult.Failure("missing-result", "Handler did not return a structured result.");
                    if (execution.IsCancellationRequested)
                        result = deadline.IsCancellationRequested ? ConsoleResult.TimedOut() : ConsoleResult.Cancelled("Cancellation was requested; completed side effects are not rolled back.");
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
            string before = line.Substring(0, caret);
            if (!ConsoleLexer.TryRead(before, out var tokens, out _, true)) return empty;
            bool newToken = tokens.Count == 0 || tokens[tokens.Count - 1].Start + tokens[tokens.Count - 1].Length < caret;
            int tokenIndex = newToken ? tokens.Count : tokens.Count - 1;
            int start = newToken ? caret : tokens[tokenIndex].Start;
            string prefix = newToken ? string.Empty : tokens[tokenIndex].Text;
            var candidates = new List<ConsoleCompletionItem>();
            if (tokenIndex == 0)
            {
                foreach (ConsoleCommand command in GetCommands(caller))
                    foreach (string name in command.Aliases.Prepend(command.Name))
                        if (name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                            candidates.Add(new ConsoleCompletionItem(command.Id + ":" + name, name, name, command.Description));
            }
            else if (Registry.TryResolve(tokens[0].Text, out var entry) && policy.Check(entry.Binding.Command, caller).IsSuccess && entry.TryAcquire(out var ownerToken))
            {
                try
                {
                    ConsoleCommand command = entry.Binding.Command;
                    int parameterIndex = tokenIndex - 1;
                    if (command.Parameters.Count == 0) return empty;
                    if (parameterIndex >= command.Parameters.Count)
                    {
                        if (!command.Parameters[command.Parameters.Count - 1].IsVariadic) return empty;
                        parameterIndex = command.Parameters.Count - 1;
                    }
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, ownerToken, lifetimeToken))
                    {
                        IReadOnlyList<ConsoleCompletionItem> supplied = null;
                        if (entry.Binding.Completer != null)
                        {
                            var context = new ConsoleCompletionContext(command, caller, line, caret, parameterIndex, prefix, limit);
                            supplied = await entry.Binding.Completer(context, linked.Token).ConfigureAwait(false);
                            linked.Token.ThrowIfCancellationRequested();
                        }
                        if (supplied != null)
                        {
                            foreach (var candidate in supplied)
                            {
                                if (candidate != null && (candidate.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                                    candidate.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) candidates.Add(candidate);
                                if (candidates.Count == limit) break;
                            }
                        }
                        else
                        {
                            Type type = command.Parameters[parameterIndex].ValueType;
                            if (command.Parameters[parameterIndex].IsVariadic) type = type.GetElementType();
                            bool nullable = Nullable.GetUnderlyingType(type) != null;
                            type = Nullable.GetUnderlyingType(type) ?? type;
                            string[] values = type == typeof(bool) ? new[] { "false", "true" } : type.IsEnum ? Enum.GetNames(type) : Array.Empty<string>();
                            if (nullable) values = values.Concat(new[] { "null" }).ToArray();
                            foreach (string value in values)
                                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) candidates.Add(new ConsoleCompletionItem(command.Id + ":" + parameterIndex + ":" + value, value));
                        }
                    }
                }
                finally { entry.Release(); }
            }
            var items = candidates.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(item => tokenIndex == 0 && !item.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Value, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.Ordinal).Take(limit).ToArray();
            int end = caret;
            if (!newToken && ConsoleLexer.TryRead(line, out var completeTokens, out _, true))
                foreach (var token in completeTokens)
                    if (token.Start == start) { end = token.Start + token.Length; break; }
            return new ConsoleCompletions(start, end - start, items, line);
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                lifetime.Cancel();
                lifetime.Dispose();
                output.Clear(); history.Clear(); outputRevision++;
            }
        }
    }
}
