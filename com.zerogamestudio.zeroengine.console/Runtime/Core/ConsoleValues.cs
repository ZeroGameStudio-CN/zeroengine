using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZeroEngine.Console
{
    public delegate bool ConsoleValueConverter(ConsoleToken token, Type target, out object value, out string error);

    public sealed class ConsoleValues
    {
        private readonly Dictionary<Type, ConsoleValueConverter> converters = new Dictionary<Type, ConsoleValueConverter>();
        private readonly object sync = new object();

        public IDisposable Register(Type type, ConsoleValueConverter converter)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            lock (sync)
            {
                if (converters.ContainsKey(type)) throw new InvalidOperationException("Converter already registered: " + type.FullName);
                converters.Add(type, converter);
            }
            return new ConverterLease(this, type, converter);
        }

        public bool TryConvert(ConsoleToken token, Type target, out object value, out string error)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            ConsoleValueConverter converter;
            lock (sync) converters.TryGetValue(target, out converter);
            if (converter != null) return converter(token, target, out value, out error);
            value = null;
            error = null;
            Type nullable = Nullable.GetUnderlyingType(target);
            if (nullable != null)
            {
                if (string.Equals(token.Raw, "null", StringComparison.OrdinalIgnoreCase)) return true;
                return TryConvert(token, nullable, out value, out error);
            }
            string text = token.Text;
            if (target == typeof(string)) { value = text; return true; }
            if (target == typeof(bool) && bool.TryParse(text, out bool boolean)) { value = boolean; return true; }
            if (target == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) { value = integer; return true; }
            if (target == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longInteger)) { value = longInteger; return true; }
            if (target == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint unsignedInteger)) { value = unsignedInteger; return true; }
            if (target == typeof(ulong) && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsignedLong)) { value = unsignedLong; return true; }
            if (target == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float single) && !float.IsNaN(single) && !float.IsInfinity(single)) { value = single; return true; }
            if (target == typeof(double) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) && !double.IsNaN(real) && !double.IsInfinity(real)) { value = real; return true; }
            if (target == typeof(decimal) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal precise)) { value = precise; return true; }
            if (target == typeof(Guid) && Guid.TryParse(text, out Guid guid)) { value = guid; return true; }
            if (target.IsEnum)
            {
                try { value = Enum.Parse(target, text, true); return true; }
                catch (ArgumentException) { }
                catch (OverflowException) { }
            }
            if (target.IsArray && target.GetArrayRank() == 1)
            {
                string raw = token.Raw;
                if (raw.Length < 2 || raw[0] != '[' || raw[raw.Length - 1] != ']')
                { error = "Array values require [item, item] syntax."; return false; }
                if (!ConsoleLexer.Read(raw.Substring(1, raw.Length - 2), out var elements, out var parseError, false, true))
                { error = parseError.Message; return false; }
                Type elementType = target.GetElementType();
                Array array = Array.CreateInstance(elementType, elements.Count);
                for (int i = 0; i < elements.Count; i++)
                {
                    if (!TryConvert(elements[i], elementType, out object element, out error))
                    { error = "Element " + i + ": " + error; return false; }
                    array.SetValue(element, i);
                }
                value = array;
                return true;
            }
            error = "Invalid " + target.Name + " value: " + text;
            return false;
        }

        internal ConsoleResult Bind(ConsoleCommand command, IReadOnlyList<ConsoleToken> tokens, out ConsoleArguments arguments)
        {
            var values = new object[command.Parameters.Count];
            int cursor = 0;
            for (int i = 0; i < values.Length; i++)
            {
                ConsoleParameter parameter = command.Parameters[i];
                if (parameter.IsVariadic)
                {
                    Type element = parameter.ValueType.GetElementType();
                    var array = Array.CreateInstance(element, tokens.Count - cursor);
                    for (int j = 0; cursor < tokens.Count; cursor++, j++)
                    {
                        if (!TryConvert(tokens[cursor], element, out object item, out string error))
                        { arguments = null; return ConsoleResult.Failure("invalid-argument", parameter.Name + ": " + error, tokens[cursor].Start); }
                        array.SetValue(item, j);
                    }
                    values[i] = array;
                }
                else if (cursor == tokens.Count)
                {
                    if (!parameter.IsOptional)
                    { arguments = null; return ConsoleResult.Failure("missing-argument", "Missing argument: " + parameter.Name); }
                    values[i] = parameter.DefaultValue;
                }
                else
                {
                    if (!TryConvert(tokens[cursor], parameter.ValueType, out values[i], out string error))
                    { arguments = null; return ConsoleResult.Failure("invalid-argument", parameter.Name + ": " + error, tokens[cursor].Start); }
                    cursor++;
                }
            }
            if (cursor != tokens.Count)
            { arguments = null; return ConsoleResult.Failure("too-many-arguments", "Too many arguments.", tokens[cursor].Start); }
            arguments = new ConsoleArguments(values);
            return ConsoleResult.Success();
        }

        internal ConsoleResult BindTyped(ConsoleCommand command, object[] supplied, out ConsoleArguments arguments)
        {
            supplied ??= Array.Empty<object>();
            var values = new object[command.Parameters.Count];
            if (supplied.Length > values.Length)
            { arguments = null; return ConsoleResult.Failure("too-many-arguments", "Too many typed arguments."); }
            for (int i = 0; i < values.Length; i++)
            {
                ConsoleParameter parameter = command.Parameters[i];
                object value;
                if (i < supplied.Length) value = supplied[i];
                else if (parameter.IsVariadic) value = Array.CreateInstance(parameter.ValueType.GetElementType(), 0);
                else if (parameter.IsOptional) value = parameter.DefaultValue;
                else { arguments = null; return ConsoleResult.Failure("missing-argument", "Missing argument: " + parameter.Name); }
                bool valid = value != null ? parameter.ValueType.IsInstanceOfType(value) : !parameter.ValueType.IsValueType || Nullable.GetUnderlyingType(parameter.ValueType) != null;
                if (!valid) { arguments = null; return ConsoleResult.Failure("invalid-argument-type", "Incorrect type for: " + parameter.Name); }
                values[i] = value;
            }
            arguments = new ConsoleArguments(values);
            return ConsoleResult.Success();
        }

        private sealed class ConverterLease : IDisposable
        {
            private ConsoleValues owner;
            private readonly Type type;
            private readonly ConsoleValueConverter converter;
            public ConverterLease(ConsoleValues owner, Type type, ConsoleValueConverter converter)
            { this.owner = owner; this.type = type; this.converter = converter; }
            public void Dispose()
            {
                var target = System.Threading.Interlocked.Exchange(ref owner, null);
                if (target == null) return;
                lock (target.sync)
                    if (target.converters.TryGetValue(type, out var current) && current == converter) target.converters.Remove(type);
            }
        }
    }
}
