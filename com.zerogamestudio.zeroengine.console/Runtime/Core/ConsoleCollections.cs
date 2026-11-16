using System;
using System.Collections;
using System.Collections.Generic;

namespace ZeroEngine.Console
{
    internal static class ConsoleCollections
    {
        internal static bool Supports(Type type)
        {
            if (!type.IsGenericType) return false;
            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(List<>) || definition == typeof(IList<>) ||
                definition == typeof(ICollection<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(HashSet<>) || definition == typeof(ISet<>) ||
                definition == typeof(Queue<>) || definition == typeof(Stack<>) ||
                definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) ||
                definition == typeof(IReadOnlyDictionary<,>);
        }

        internal static bool TryConvert(ConsoleValues converters, ConsoleToken token, Type target,
            out object value, out string error)
        {
            value = null; error = null;
            var arguments = target.GetGenericArguments();
            string raw = token.Raw;
            if (raw == "null") return true;
            if (raw.Length < 2 || raw[0] != '[' || raw[raw.Length - 1] != ']')
            { error = "Collection values require [item, item] syntax."; return false; }
            if (!ConsoleLexer.Read(raw.Substring(1, raw.Length - 2), out var items, out var parseError, false, true))
            { error = parseError.Message; return false; }
            var definition = target.GetGenericTypeDefinition();
            bool dictionary = arguments.Length == 2;
            if (dictionary)
            {
                var result = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
                for (int i = 0; i < items.Count; i++)
                {
                    string pair = items[i].Raw;
                    if (pair.Length < 2 || pair[0] != '(' || pair[pair.Length - 1] != ')' ||
                        !ConsoleLexer.Read(pair.Substring(1, pair.Length - 2), out var members, out parseError, false, true))
                    { error = "Dictionary entries require (key, value) syntax."; return false; }
                    if (members.Count != 2) { error = "Dictionary entries require exactly one key and value."; return false; }
                    if (!converters.TryConvert(members[0], arguments[0], out var key, out error) ||
                        !converters.TryConvert(members[1], arguments[1], out var item, out error))
                    { error = "Entry " + i + ": " + error; return false; }
                    if (key == null) { error = "Dictionary keys cannot be null."; return false; }
                    if (result.Contains(key)) { error = "Duplicate dictionary key at entry " + i + "."; return false; }
                    result.Add(key, item);
                }
                value = result;
                return true;
            }
            var array = Array.CreateInstance(arguments[0], items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (!converters.TryConvert(items[i], arguments[0], out var item, out error))
                { error = "Element " + i + ": " + error; return false; }
                array.SetValue(item, i);
            }
            Type concrete = target;
            if (target.IsInterface)
                concrete = (definition == typeof(ISet<>) ? typeof(HashSet<>) : typeof(List<>)).MakeGenericType(arguments);
            value = Activator.CreateInstance(concrete, new object[] { array });
            return true;
        }
    }
}
