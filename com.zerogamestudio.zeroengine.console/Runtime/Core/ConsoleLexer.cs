using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroEngine.Console
{
    public readonly struct ConsoleToken
    {
        public string Text { get; }
        public string Raw { get; }
        public int Start { get; }
        public int Length => Raw.Length;
        public ConsoleToken(string text, string raw, int start) { Text = text; Raw = raw; Start = start; }
    }

    public static class ConsoleLexer
    {
        public const int MaximumLineLength = 8192;

        public static bool TryRead(string line, out IReadOnlyList<ConsoleToken> tokens, out ConsoleResult error,
            bool allowIncomplete = false) => Read(line, out tokens, out error, allowIncomplete, false);

        internal static bool Read(string line, out IReadOnlyList<ConsoleToken> tokens, out ConsoleResult error,
            bool allowIncomplete, bool commaSeparates)
        {
            line ??= string.Empty;
            var result = new List<ConsoleToken>();
            tokens = result;
            error = null;
            if (line.Length > MaximumLineLength)
            {
                error = ConsoleResult.Failure("line-too-long", "The command exceeds the input limit.", MaximumLineLength);
                return false;
            }
            int index = 0;
            Span<char> groups = stackalloc char[32];
            while (index < line.Length)
            {
                while (index < line.Length && (char.IsWhiteSpace(line[index]) || commaSeparates && line[index] == ',')) index++;
                if (index == line.Length) break;
                int start = index;
                StringBuilder value = null;
                int depth = 0;
                char quote = '\0';
                int quoteStart = -1;
                while (index < line.Length)
                {
                    char c = line[index];
                    if (quote == '\0' && depth == 0 && (char.IsWhiteSpace(c) || commaSeparates && c == ',')) break;
                    if (c == '\\')
                    {
                        if (index + 1 < line.Length)
                        {
                            char next = line[index + 1];
                            if (next == '\\' || next == '"' || next == '\'' || char.IsWhiteSpace(next))
                            {
                                if (value == null) value = new StringBuilder().Append(line, start, index - start);
                                value.Append(next); index += 2; continue;
                            }
                            if (quote != '\0' && (next == 'n' || next == 'r' || next == 't'))
                            { value.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : '\t'); index += 2; continue; }
                        }
                        value?.Append(c); index++; continue;
                    }
                    if (quote != '\0')
                    {
                        if (c == quote) quote = '\0';
                        else value.Append(c);
                        index++; continue;
                    }
                    if (c == '"' || c == '\'')
                    {
                        if (value == null) value = new StringBuilder().Append(line, start, index - start);
                        quote = c; quoteStart = index++; continue;
                    }
                    if (c == '[' || c == '(' || c == '{')
                    {
                        if (depth == groups.Length)
                        { error = ConsoleResult.Failure("group-too-deep", "Argument nesting exceeds the limit.", index); return false; }
                        groups[depth++] = c;
                    }
                    else if (c == ']' || c == ')' || c == '}')
                    {
                        char expected = c == ']' ? '[' : c == ')' ? '(' : '{';
                        if (depth == 0 || groups[--depth] != expected)
                        { error = ConsoleResult.Failure("unexpected-group-end", "Mismatched closing delimiter.", index); return false; }
                    }
                    value?.Append(c); index++;
                }
                if (!allowIncomplete && quote != '\0')
                { error = ConsoleResult.Failure("unterminated-quote", "Unterminated quoted argument.", quoteStart); return false; }
                if (!allowIncomplete && depth != 0)
                { error = ConsoleResult.Failure("unterminated-group", "Unterminated argument group.", start); return false; }
                string raw = line.Substring(start, index - start);
                result.Add(new ConsoleToken(value == null ? raw : value.ToString(), raw, start));
                if (result.Count > 256)
                { error = ConsoleResult.Failure("too-many-tokens", "The command exceeds the token limit.", start); return false; }
            }
            return true;
        }

        public static string Quote(string value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
        }
    }
}
