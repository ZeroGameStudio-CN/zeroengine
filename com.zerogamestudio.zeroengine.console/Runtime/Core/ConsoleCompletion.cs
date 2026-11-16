using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public sealed class ConsoleCompletionItem
    {
        public string Id { get; }
        public string Value { get; }
        public string Label { get; }
        public string Description { get; }
        public ConsoleCompletionItem(string id, string value, string label = null, string description = "")
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Value = value ?? string.Empty;
            Label = label ?? Value;
            Description = description ?? string.Empty;
        }
    }

    public sealed class ConsoleCompletionContext
    {
        public ConsoleCommand Command { get; }
        public ConsoleCaller Caller { get; }
        public string Line { get; }
        public int Caret { get; }
        public int ParameterIndex { get; }
        public string Prefix { get; }
        public int Limit { get; }
        internal ConsoleCompletionContext(ConsoleCommand command, ConsoleCaller caller, string line, int caret,
            int parameterIndex, string prefix, int limit)
        { Command = command; Caller = caller; Line = line; Caret = caret; ParameterIndex = parameterIndex; Prefix = prefix; Limit = limit; }
    }

    public sealed class ConsoleCompletions
    {
        public int Start { get; }
        public int Length { get; }
        public IReadOnlyList<ConsoleCompletionItem> Items { get; }
        public ConsoleCommandHint Hint { get; }
        private readonly string source;
        internal ConsoleCompletions(int start, int length, IReadOnlyList<ConsoleCompletionItem> items, string source)
            : this(start, length, items, source, null) { }
        internal ConsoleCompletions(int start, int length, IReadOnlyList<ConsoleCompletionItem> items, string source, ConsoleCommandHint hint)
        { Start = start; Length = length; Items = items; this.source = source; Hint = hint; }

        public string Apply(string line, int index)
            => Apply(line, index, out _);

        public string Apply(string line, int index, out int caret)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));
            if (!string.Equals(line, source, StringComparison.Ordinal)) throw new InvalidOperationException("Completion input changed.");
            if (Start < 0 || Length < 0 || Start + Length > line.Length) throw new InvalidOperationException("Completion input changed.");
            string value = Items[index].Value;
            bool quote = value.Length == 0;
            foreach (char c in value) quote |= char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '\\' || c == '[' || c == ']' || c == '(' || c == ')';
            string inserted = quote ? ConsoleLexer.Quote(value) : value;
            caret = Start + inserted.Length;
            return line.Substring(0, Start) + inserted + line.Substring(Start + Length);
        }
    }

    public delegate Task<IReadOnlyList<ConsoleCompletionItem>> ConsoleCompleter(ConsoleCompletionContext context, CancellationToken cancellation);
}
