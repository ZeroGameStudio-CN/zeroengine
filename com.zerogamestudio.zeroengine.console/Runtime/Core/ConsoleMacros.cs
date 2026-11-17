using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroEngine.Console
{
    public sealed class ConsoleMacros
    {
        public const int MaximumExpansions = 16;
        public const int MaximumDefinitions = 256;
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly object sync = new object();
        private string[] names;
        private long revision;
        public long Revision { get { lock (sync) return revision; } }

        public void Define(string name, string replacement)
        {
            if (!ValidName(name)) throw new ArgumentException("A macro name requires letters, digits, '.', '_' or '-'.", nameof(name));
            if (replacement == null || replacement.Length > ConsoleLexer.MaximumLineLength || replacement.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("A macro requires one bounded line.", nameof(replacement));
            lock (sync)
            {
                if (values.Count == MaximumDefinitions && !values.ContainsKey(name))
                    throw new InvalidOperationException("The session macro limit has been reached.");
                values[name] = replacement; names = null; revision++;
            }
        }

        public bool Remove(string name)
        {
            lock (sync) { if (!values.Remove(name)) return false; names = null; revision++; return true; }
        }
        public void Clear() { lock (sync) { if (values.Count == 0) return; values.Clear(); names = null; revision++; } }

        internal string[] Names() { lock (sync) return names ??= values.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray(); }

        internal static bool NameSpan(string line, int caret, out int start, out int length, out string prefix)
        {
            start = caret; length = 0; prefix = null;
            int first = caret;
            while (first > 0 && NameCharacter(line[first - 1])) first--;
            if (first == 0 || line[first - 1] != '#' || first > 1 && line[first - 2] == '\\') return false;
            int end = caret;
            while (end < line.Length && NameCharacter(line[end])) end++;
            start = first - 1; length = end - start; prefix = line.Substring(first, caret - first);
            return true;
        }

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
