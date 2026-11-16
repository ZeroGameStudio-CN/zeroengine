using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ZeroEngine.Console
{
    public interface IConsoleValueFormatter { string Format(object value); }

    public class ConsoleValueFormatter : IConsoleValueFormatter
    {
        public static ConsoleValueFormatter Default { get; } = new ConsoleValueFormatter();
        private readonly int maximumDepth, maximumItems, maximumLength;

        public ConsoleValueFormatter(int maximumDepth = 4, int maximumItems = 32, int maximumLength = 2048)
        {
            if (maximumDepth < 1 || maximumDepth > 32) throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            if (maximumItems < 1 || maximumItems > 1024) throw new ArgumentOutOfRangeException(nameof(maximumItems));
            if (maximumLength < 16 || maximumLength > 65536) throw new ArgumentOutOfRangeException(nameof(maximumLength));
            this.maximumDepth = maximumDepth; this.maximumItems = maximumItems; this.maximumLength = maximumLength;
        }

        public string Format(object value)
        {
            if (value == null) return "null";
            if (value is string text && text.Length > 0) return Limit(text, maximumLength);
            if (TryFormatAtom(value, out string atom)) return Limit(atom ?? string.Empty, maximumLength);
            var writer = new Writer(this);
            writer.Write(value, 0);
            return writer.ToString();
        }

        protected virtual bool TryFormatAtom(object value, out string text)
        {
            if (value is bool boolean) { text = boolean ? "true" : "false"; return true; }
            if (value is char character) { text = ConsoleLexer.Quote(character.ToString()); return true; }
            if (value is DateTime date) { text = date.ToString("O", CultureInfo.InvariantCulture); return true; }
            if (value is DateTimeOffset offset) { text = offset.ToString("O", CultureInfo.InvariantCulture); return true; }
            if (value is TimeSpan duration) { text = duration.ToString("c", CultureInfo.InvariantCulture); return true; }
            if (value is Guid guid) { text = guid.ToString("D"); return true; }
            Type type = value.GetType();
            if (type.IsEnum) { text = value.ToString(); return true; }
            if ((type.IsPrimitive || value is decimal) && value is IFormattable formattable)
            {
                text = formattable.ToString(value is float || value is double ? "R" : null, CultureInfo.InvariantCulture);
                return true;
            }
            text = null;
            return false;
        }

        private static string Limit(string value, int maximum)
        {
            if (value.Length <= maximum) return value;
            int count = maximum - 1;
            if (count > 0 && char.IsHighSurrogate(value[count - 1])) count--;
            return value.Substring(0, count) + "…";
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }

        private sealed class Writer
        {
            private readonly ConsoleValueFormatter formatter;
            private readonly StringBuilder text = new StringBuilder(128);
            private readonly HashSet<object> ancestors = new HashSet<object>(ReferenceComparer.Instance);
            private int remaining;
            private bool truncated;
            public Writer(ConsoleValueFormatter formatter) { this.formatter = formatter; remaining = formatter.maximumItems; }
            public override string ToString() => text.ToString();

            private void Append(string value) => Append(value, 0, value.Length);
            private void Append(string value, int start, int length)
            {
                if (truncated) return;
                int available = formatter.maximumLength - text.Length;
                if (length <= available) { text.Append(value, start, length); return; }
                int count = Math.Max(0, available - 1);
                if (count > 0 && char.IsHighSurrogate(value[start + count - 1])) count--;
                text.Append(value, start, count);
                if (text.Length == formatter.maximumLength) text.Length--;
                if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1])) text.Length--;
                text.Append('…'); truncated = true;
            }

            private void Quoted(string value)
            {
                Append("\"");
                int start = 0;
                for (int i = 0; i < Math.Min(value.Length, formatter.maximumLength + 1) && !truncated; i++)
                {
                    string escaped;
                    switch (value[i])
                    {
                        case '\\': escaped = "\\\\"; break;
                        case '"': escaped = "\\\""; break;
                        case '\n': escaped = "\\n"; break;
                        case '\r': escaped = "\\r"; break;
                        case '\t': escaped = "\\t"; break;
                        default: continue;
                    }
                    Append(value, start, i - start); Append(escaped); start = i + 1;
                }
                Append(value, start, value.Length - start);
                Append("\"");
            }

            public void Write(object value, int depth)
            {
                if (truncated) return;
                if (value == null) { Append("null"); return; }
                if (value is string literal) { Quoted(literal); return; }
                if (formatter.TryFormatAtom(value, out string atom)) { Append(atom ?? string.Empty); return; }
                if (value is ITuple tuple)
                {
                    if (depth >= formatter.maximumDepth) { Append("…"); return; }
                    Append("(");
                    for (int i = 0; i < tuple.Length && !truncated; i++)
                    {
                        if (i > 0) Append(", ");
                        if (remaining == 0) { Append("…"); break; }
                        remaining--; Write(tuple[i], depth + 1);
                    }
                    Append(")"); return;
                }
                if (!(value is IEnumerable sequence)) { Append("(" + value.GetType().Name + ")"); return; }
                if (depth >= formatter.maximumDepth) { Append("…"); return; }
                if (!ancestors.Add(value)) { Append("(cycle)"); return; }
                var dictionary = value as IDictionary;
                Append(dictionary != null ? "{" : "[");
                IEnumerator iterator = null;
                try
                {
                    iterator = dictionary != null ? dictionary.GetEnumerator() : sequence.GetEnumerator();
                    bool first = true;
                    while (!truncated && iterator.MoveNext())
                    {
                        if (!first) Append(", ");
                        if (remaining == 0) { Append("…"); break; }
                        remaining--; first = false;
                        if (dictionary != null)
                        {
                            DictionaryEntry entry = ((IDictionaryEnumerator)iterator).Entry;
                            Write(entry.Key, depth + 1); Append(": "); Write(entry.Value, depth + 1);
                        }
                        else Write(iterator.Current, depth + 1);
                    }
                }
                catch (Exception) { Append("(format-error)"); }
                finally
                {
                    try { (iterator as IDisposable)?.Dispose(); }
                    finally { ancestors.Remove(value); }
                }
                Append(dictionary != null ? "}" : "]");
            }
        }
    }
}
