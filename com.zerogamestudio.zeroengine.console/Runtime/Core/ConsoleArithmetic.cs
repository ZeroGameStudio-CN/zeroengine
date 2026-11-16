using System;
using System.Globalization;

namespace ZeroEngine.Console
{
    internal static class ConsoleArithmetic
    {
        internal static bool Supports(Type type) => type == typeof(byte) || type == typeof(sbyte) ||
            type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

        internal static bool TryConvert(string input, Type type, out object value, out string error)
        {
            value = null; error = null;
            try
            {
                var parser = new Parser(input, type);
                var result = parser.Parse();
                value = parser.Floating ? Convert.ChangeType(result.Float, type, CultureInfo.InvariantCulture)
                    : Convert.ChangeType(result.Exact, type, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException failure) { error = failure.Message; return false; }
            catch (OverflowException) { error = "Arithmetic value exceeds the target numeric range."; return false; }
            catch (DivideByZeroException) { error = "Arithmetic division by zero."; return false; }
        }

        private readonly struct Number
        {
            public readonly decimal Exact;
            public readonly double Float;
            public Number(decimal exact, double floating) { Exact = exact; Float = floating; }
        }

        private ref struct Parser
        {
            private readonly ReadOnlySpan<char> input;
            private readonly Type target;
            private readonly bool integral;
            private int position, depth, operations;
            public bool Floating { get; }

            public Parser(string text, Type type)
            {
                if (text == null || text.Length > ConsoleLexer.MaximumLineLength) throw new FormatException("Arithmetic input exceeds its limit.");
                input = text.AsSpan(); target = type;
                Floating = type == typeof(float) || type == typeof(double);
                integral = !Floating && type != typeof(decimal);
                position = depth = operations = 0;
            }

            public Number Parse()
            {
                var value = Expression(0); Space();
                if (position != input.Length) throw new FormatException("Unexpected arithmetic input at " + position + ".");
                return value;
            }

            private Number Expression(int minimum)
            {
                if (++depth > 32) throw new FormatException("Arithmetic nesting exceeds its limit.");
                try
                {
                    var left = Atom();
                    while (true)
                    {
                        Space();
                        if (position == input.Length || input[position] == ')') return left;
                        char operation = input[position];
                        int precedence = operation == '+' || operation == '-' ? 1 : operation == '*' || operation == '/' || operation == '%' ? 2 : -1;
                        if (precedence < minimum) return left;
                        if (++operations > 256) throw new FormatException("Arithmetic operation count exceeds its limit.");
                        position++;
                        var right = Expression(precedence + 1);
                        if (Floating)
                        {
                            double value = operation == '+' ? left.Float + right.Float : operation == '-' ? left.Float - right.Float :
                                operation == '*' ? left.Float * right.Float : operation == '/' ? left.Float / right.Float : left.Float % right.Float;
                            left = Checked(new Number(0, value));
                        }
                        else
                        {
                            decimal value = operation == '+' ? left.Exact + right.Exact : operation == '-' ? left.Exact - right.Exact :
                                operation == '*' ? left.Exact * right.Exact : operation == '/' ? left.Exact / right.Exact : left.Exact % right.Exact;
                            if (integral && operation == '/') value = decimal.Truncate(value);
                            left = Checked(new Number(value, 0));
                        }
                    }
                }
                finally { depth--; }
            }

            private Number Atom()
            {
                Space();
                if (position == input.Length) throw new FormatException("Arithmetic value is missing.");
                char first = input[position];
                if (first == '+' || first == '-')
                {
                    if (++operations > 256) throw new FormatException("Arithmetic operation count exceeds its limit.");
                    position++;
                    if (++depth > 32) throw new FormatException("Arithmetic nesting exceeds its limit.");
                    Number result;
                    try { result = Atom(); } finally { depth--; }
                    return first == '+' ? result : Checked(new Number(-result.Exact, -result.Float));
                }
                if (first == '(')
                {
                    position++; var result = Expression(0); Space();
                    if (position == input.Length || input[position++] != ')') throw new FormatException("Arithmetic parentheses are incomplete.");
                    return result;
                }
                int start = position;
                while (position < input.Length)
                {
                    char character = input[position];
                    if (char.IsDigit(character) || character == '.' || character == 'e' || character == 'E') { position++; continue; }
                    if ((character == '+' || character == '-') && position > start && (input[position - 1] == 'e' || input[position - 1] == 'E')) { position++; continue; }
                    break;
                }
                var text = input.Slice(start, position - start);
                if (Floating)
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) throw new FormatException("Invalid arithmetic number.");
                    return Checked(new Number(0, value));
                }
                if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact)) throw new FormatException("Invalid arithmetic number.");
                if (integral && exact != decimal.Truncate(exact)) throw new FormatException("Integral arithmetic requires integral literals.");
                // Range is checked after unary signs, permitting the minimum signed value.
                return new Number(exact, 0);
            }

            private Number Checked(Number value)
            {
                if (Floating)
                {
                    double floating = target == typeof(float) ? (float)value.Float : value.Float;
                    if (double.IsNaN(floating) || double.IsInfinity(floating)) throw new OverflowException();
                    return new Number(0, floating);
                }
                if (integral) Convert.ChangeType(value.Exact, target, CultureInfo.InvariantCulture);
                return value;
            }

            private void Space() { while (position < input.Length && char.IsWhiteSpace(input[position])) position++; }
        }
    }
}
