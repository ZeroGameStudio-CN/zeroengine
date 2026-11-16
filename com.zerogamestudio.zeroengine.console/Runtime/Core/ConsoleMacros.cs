using System;
using System.Collections.Generic;
using System.Text;

namespace ZeroEngine.Console
{
    public sealed class ConsoleMacros
    {
        public const int MaximumExpansions = 16;
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object sync = new object();

        public void Define(string name, string replacement)
        {
            if (!ValidName(name)) throw new ArgumentException("A macro name requires letters, digits, '.', '_' or '-'.", nameof(name));
            if (replacement == null || replacement.Length > ConsoleLexer.MaximumLineLength || replacement.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("A macro requires one bounded line.", nameof(replacement));
            lock (sync) values[name] = replacement;
        }

        public bool Remove(string name) { lock (sync) return values.Remove(name); }
        public void Clear() { lock (sync) values.Clear(); }

        public IReadOnlyDictionary<string, string> Snapshot()
        {
            lock (sync) return new Dictionary<string, string>(values, StringComparer.Ordinal);
        }

        public bool TryExpand(string line, out string expanded, out string error)
        {
            expanded = line ?? string.Empty; error = null;
            if (expanded.Length > ConsoleLexer.MaximumLineLength) { error = "The expanded input exceeds the line limit."; return false; }
            lock (sync)
            {
                if (values.Count == 0 || expanded.IndexOf('#') < 0) return true;
                for (int pass = 0; pass <= MaximumExpansions; pass++)
                {
                    StringBuilder output = null;
                    int copied = 0;
                    for (int index = 0; index < expanded.Length; index++)
                    {
                        if (expanded[index] != '#' || index > 0 && expanded[index - 1] == '\\') continue;
                        int end = index + 1;
                        while (end < expanded.Length && NameCharacter(expanded[end])) end++;
                        if (end == index + 1) continue;
                        string name = expanded.Substring(index + 1, end - index - 1);
                        if (!values.TryGetValue(name, out var replacement)) { index = end - 1; continue; }
                        if (pass == MaximumExpansions) { error = "Macro expansion exceeds its recursion limit."; return false; }
                        output ??= new StringBuilder(Math.Min(ConsoleLexer.MaximumLineLength, expanded.Length + replacement.Length));
                        int projected = output.Length + index - copied + replacement.Length + expanded.Length - end;
                        if (projected > ConsoleLexer.MaximumLineLength) { error = "The expanded input exceeds the line limit."; return false; }
                        output.Append(expanded, copied, index - copied).Append(replacement);
                        copied = end; index = end - 1;
                    }
                    if (output == null) return true;
                    output.Append(expanded, copied, expanded.Length - copied);
                    expanded = output.ToString();
                }
            }
            error = "Macro expansion exceeds its recursion limit.";
            return false;
        }

        private static bool NameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.';
        private static bool ValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
            foreach (char character in name) if (!NameCharacter(character)) return false;
            return true;
        }
    }
}
