using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroEngine.Console
{
    internal static class ConsoleOverloads
    {
        internal static ConsoleResult Select(ConsoleEntry[] entries, ConsoleValues values, IConsolePolicy policy,
            ConsoleCaller caller, IReadOnlyList<ConsoleToken> tokens, object[] supplied,
            out ConsoleEntry selected, out ConsoleArguments arguments)
        {
            selected = null; arguments = null;
            ConsoleResult denied = null;
            var visible = new List<ConsoleEntry>();
            int count = tokens != null ? tokens.Count - 1 : supplied.Length;
            Score best = default;
            bool ambiguous = false;
            foreach (var entry in entries)
            {
                if (!entry.IsActive) continue;
                var command = entry.Binding.Command;
                var allowed = policy.Check(command, caller);
                if (!allowed.IsSuccess) { denied ??= allowed; continue; }
                visible.Add(entry);
                int required = 0;
                foreach (var parameter in command.Parameters) if (!parameter.IsOptional) required++;
                bool variadic = command.Parameters.Count > 0 && command.Parameters[command.Parameters.Count - 1].IsVariadic;
                if (count < required || count > command.Parameters.Count && (tokens == null || !variadic)) continue;
                ConsoleArguments bound;
                ConsoleResult converted;
                try { converted = tokens != null ? values.Bind(command, tokens, 1, out bound) : values.BindTyped(command, supplied, out bound); }
                catch (Exception error) { return ConsoleResult.Failure("overload-conversion-failed", error.Message); }
                if (!converted.IsSuccess) continue;
                int defaults = Math.Max(0, command.Parameters.Count - count - (variadic ? 1 : 0));
                var score = new Score(variadic ? 2 : defaults > 0 ? 1 : 0, defaults, ConversionCost(command, tokens, supplied));
                int comparison = selected == null ? -1 : score.CompareTo(best);
                if (comparison < 0) { selected = entry; arguments = bound; best = score; ambiguous = false; }
                else if (comparison == 0) ambiguous = true;
            }
            foreach (var entry in visible)
            {
                var allowed = entry.IsActive ? policy.Check(entry.Binding.Command, caller) :
                    ConsoleResult.Failure("module-unloaded", "Command module was unloaded.");
                if (!allowed.IsSuccess) { selected = null; arguments = null; return allowed; }
            }
            if (visible.Count == 0) return denied ?? ConsoleResult.Failure("module-unloaded", "Command module was unloaded.");
            if (selected != null && !ambiguous) return ConsoleResult.Success();
            arguments = null; selected = null;
            var message = new StringBuilder(ambiguous ? "Ambiguous overload; use an explicit command ID:" : "No matching overload:");
            foreach (var entry in visible)
            {
                var command = entry.Binding.Command;
                if (message.Length + command.Signature.Length + command.Id.Length > 4096) break;
                message.Append("\n").Append(command.Id).Append(" — ").Append(command.Signature);
            }
            return ConsoleResult.Failure(ambiguous ? "ambiguous-overload" : "no-matching-overload", message.ToString());
        }

        internal static ConsoleEntry[] Hints(ConsoleEntry[] entries, IConsolePolicy policy, ConsoleCaller caller, int tokenIndex)
        {
            var visible = entries.Where(entry => entry.IsActive && policy.Check(entry.Binding.Command, caller).IsSuccess).ToArray();
            if (tokenIndex <= 0) return visible;
            var fitting = visible.Where(entry => entry.Binding.Command.Parameters.Count >= tokenIndex ||
                entry.Binding.Command.Parameters.Count > 0 && entry.Binding.Command.Parameters.Last().IsVariadic).ToArray();
            return fitting.Length == 0 ? visible : fitting;
        }

        private static int ConversionCost(ConsoleCommand command, IReadOnlyList<ConsoleToken> tokens, object[] supplied)
        {
            int cost = 0, count = tokens != null ? tokens.Count - 1 : supplied.Length;
            for (int i = 0; i < count; i++)
            {
                var parameter = command.Parameters[Math.Min(i, command.Parameters.Count - 1)];
                Type type = parameter.IsVariadic && tokens != null ? parameter.ValueType.GetElementType() : parameter.ValueType;
                if (tokens != null)
                {
                    string raw = tokens[i + 1].Raw;
                    bool quoted = raw.Length > 0 && (raw[0] == '"' || raw[0] == '\'');
                    if (type == typeof(string) ? !quoted : quoted) cost++;
                }
                else if (supplied[i] != null && type != supplied[i].GetType())
                    cost += type == typeof(object) ? 100 : Nullable.GetUnderlyingType(type) == supplied[i].GetType() ? 1 : 2;
                else if (supplied[i] == null && type == typeof(object)) cost += 100;
            }
            return cost;
        }

        private readonly struct Score : IComparable<Score>
        {
            private readonly int arity, defaults, conversion;
            internal Score(int arity, int defaults, int conversion) { this.arity = arity; this.defaults = defaults; this.conversion = conversion; }
            public int CompareTo(Score other)
            {
                int result = arity.CompareTo(other.arity);
                if (result == 0) result = defaults.CompareTo(other.defaults);
                return result == 0 ? conversion.CompareTo(other.conversion) : result;
            }
        }
    }
}
