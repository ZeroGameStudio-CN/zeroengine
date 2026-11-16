using System;
using System.Text;

namespace ZeroEngine.Console
{
    public sealed class ConsoleCommandHint
    {
        public ConsoleCommand Command { get; }
        public string Signature => Command.Signature;
        public int ParameterIndex { get; }
        public ConsoleParameter Parameter => ParameterIndex < 0 ? null : Command.Parameters[ParameterIndex];
        public string Detail => ParameterIndex < 0 ? Command.Description : Command.ParameterHints[ParameterIndex];
        internal ConsoleCommandHint(ConsoleCommand command, int parameterIndex) { Command = command; ParameterIndex = parameterIndex; }
    }

    internal static class ConsoleCommandDisplay
    {
        internal static string TypeName(Type type)
        {
            Type nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return TypeName(nullable) + "?";
            if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(short)) return "short";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(char)) return "char";
            string name = type.Name;
            if (!type.IsGenericType) return name;
            int marker = name.IndexOf('`');
            var text = new StringBuilder(marker < 0 ? name : name.Substring(0, marker)).Append('<');
            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++) { if (i > 0) text.Append(", "); text.Append(TypeName(arguments[i])); }
            return text.Append('>').ToString();
        }

        internal static string Signature(ConsoleCommand command)
        {
            var text = new StringBuilder(command.Name);
            foreach (ConsoleParameter parameter in command.Parameters)
            {
                text.Append(parameter.IsOptional ? " [" : " <").Append(parameter.Name).Append(": ").Append(TypeName(parameter.ValueType));
                if (parameter.IsVariadic) text.Append("...");
                else if (parameter.IsOptional)
                {
                    object supplied = parameter.DefaultValue;
                    string value = supplied is string literal ? ConsoleLexer.Quote(literal.Length <= 2048 ? literal : ConsoleValueFormatter.Default.Format(literal)) :
                        supplied is System.Collections.IEnumerable || supplied is System.Runtime.CompilerServices.ITuple ? "(" + TypeName(supplied.GetType()) + ")" :
                        ConsoleValueFormatter.Default.Format(supplied);
                    text.Append(" = ").Append(value);
                }
                text.Append(parameter.IsOptional ? ']' : '>');
            }
            return text.ToString();
        }
    }
}
