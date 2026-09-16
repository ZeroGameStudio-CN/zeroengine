using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroGameStudio.ConfigPipeline.Editor
{
    // A single record of CSV, not parallel lists of multi-field objects.
    internal static class InlineListCell
    {
        internal static IReadOnlyList<string> Split(string text)
        {
            var values = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return values;
            int cursor = 0;
            while (cursor < text.Length)
            {
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                string value;
                if (cursor < text.Length && text[cursor] == '"')
                {
                    cursor++;
                    var quoted = new StringBuilder();
                    bool closed = false;
                    while (cursor < text.Length)
                    {
                        char next = text[cursor++];
                        if (next != '"') { quoted.Append(next); continue; }
                        if (cursor < text.Length && text[cursor] == '"')
                        { quoted.Append('"'); cursor++; continue; }
                        closed = true;
                        break;
                    }
                    if (!closed) throw new FormatException("List has an unclosed quote.");
                    value = quoted.ToString();
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
                    if (cursor < text.Length && !IsSeparator(text[cursor]))
                        throw new FormatException("Expected a comma after the quoted list value.");
                }
                else
                {
                    int start = cursor;
                    while (cursor < text.Length && !IsSeparator(text[cursor])) cursor++;
                    value = text.Substring(start, cursor - start).Trim();
                    if (value.Length == 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                        throw new FormatException("List contains an empty or improperly quoted value.");
                }
                values.Add(value);
                if (cursor == text.Length) break;
                cursor++;
                if (string.IsNullOrWhiteSpace(text.Substring(cursor)))
                    throw new FormatException("List must not end with a comma.");
            }
            return values;
        }

        internal static string Join(IEnumerable<string> values)
        {
            return string.Join(",", values.Select(value =>
                value.Length == 0 || value != value.Trim() || value.IndexOfAny(new[] { ',', '，', '"', '\r', '\n' }) >= 0
                    ? "\"" + value.Replace("\"", "\"\"") + "\"" : value));
        }

        private static bool IsSeparator(char value) => value == ',' || value == '，';

        internal static string Format(ConfigSchemaNode schema, ConfigNode node)
        {
            var array = (ConfigArrayNode)node;
            return Join(array.Items.Select(item =>
            {
                var record = (ConfigObjectNode)item;
                if (!record.TryGetValue(schema.InlineValueField, out ConfigNode value))
                    throw new InvalidOperationException("Inline list is missing its business value.");
                return value is ConfigStringNode text ? text.Value : CanonicalJsonWriter.WriteText(value).TrimEnd('\n');
            }));
        }
    }
}
