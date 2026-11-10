using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    [Flags]
    public enum ConsoleEnvironment
    {
        Editor = 1,
        DevelopmentPlayer = 2,
        Player = 4,
        Development = Editor | DevelopmentPlayer,
        All = Development | Player
    }

    public enum ConsoleResultStatus { Succeeded, Failed, Cancelled, TimedOut }
    public enum ConsoleOutputLevel { Info, Warning, Error }

    public sealed class ConsoleResult
    {
        public ConsoleResultStatus Status { get; }
        public string Code { get; }
        public string Message { get; }
        public object Data { get; }
        public int? Position { get; }
        public bool IsSuccess => Status == ConsoleResultStatus.Succeeded;

        private ConsoleResult(ConsoleResultStatus status, string code, string message, object data, int? position)
        {
            Status = status;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Data = data;
            Position = position;
        }

        public static ConsoleResult Success(string message = "", object data = null) =>
            new ConsoleResult(ConsoleResultStatus.Succeeded, "ok", message, data, null);
        public static ConsoleResult Failure(string code, string message, int? position = null) =>
            new ConsoleResult(ConsoleResultStatus.Failed, code, message, null, position);
        public static ConsoleResult Cancelled(string message = "Cancellation requested.") =>
            new ConsoleResult(ConsoleResultStatus.Cancelled, "cancelled", message, null, null);
        public static ConsoleResult TimedOut() =>
            new ConsoleResult(ConsoleResultStatus.TimedOut, "deadline-exceeded", "The command deadline expired.", null, null);
    }

    public sealed class ConsoleCaller
    {
        private readonly HashSet<string> capabilities;
        public ConsoleEnvironment Environment { get; }
        public string Identity { get; }

        public ConsoleCaller(ConsoleEnvironment environment, string identity = "local", IEnumerable<string> capabilities = null)
        {
            if (environment != ConsoleEnvironment.Editor && environment != ConsoleEnvironment.DevelopmentPlayer && environment != ConsoleEnvironment.Player)
                throw new ArgumentOutOfRangeException(nameof(environment), "Choose one caller environment.");
            Environment = environment;
            Identity = identity ?? string.Empty;
            this.capabilities = new HashSet<string>(capabilities ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public bool HasCapability(string capability) => capabilities.Contains(capability);
    }

    public interface IConsolePolicy
    {
        ConsoleResult Check(ConsoleCommand command, ConsoleCaller caller);
    }

    public sealed class DefaultConsolePolicy : IConsolePolicy
    {
        public ConsoleResult Check(ConsoleCommand command, ConsoleCaller caller)
        {
            if ((command.Environments & caller.Environment) == 0)
                return ConsoleResult.Failure("environment-denied", "Command unavailable in this environment.");
            foreach (string capability in command.Capabilities)
                if (!caller.HasCapability(capability))
                    return ConsoleResult.Failure("capability-denied", "Missing capability: " + capability);
            return ConsoleResult.Success();
        }
    }

    public sealed class ConsoleParameter
    {
        public string Name { get; }
        public Type ValueType { get; }
        public bool IsOptional { get; }
        public object DefaultValue { get; }
        public bool IsVariadic { get; }
        public string Description { get; }

        public ConsoleParameter(string name, Type valueType, string description = "", bool optional = false,
            object defaultValue = null, bool variadic = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A parameter name is required.", nameof(name));
            ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
            if (variadic && !valueType.IsArray) throw new ArgumentException("A variadic parameter must be an array.", nameof(variadic));
            if (defaultValue != null && !valueType.IsInstanceOfType(defaultValue))
                throw new ArgumentException("Default value does not match the parameter type.", nameof(defaultValue));
            if (optional && defaultValue == null && valueType.IsValueType && Nullable.GetUnderlyingType(valueType) == null)
                throw new ArgumentException("A non-nullable optional value needs a typed default.", nameof(defaultValue));
            Name = name;
            Description = description ?? string.Empty;
            IsOptional = optional || variadic;
            DefaultValue = defaultValue;
            IsVariadic = variadic;
        }
    }

    public sealed class ConsoleCommand
    {
        public string Id { get; }
        public string Name { get; }
        public string Module { get; }
        public string Version { get; }
        public string Description { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<string> Examples { get; }
        public IReadOnlyList<string> Capabilities { get; }
        public IReadOnlyList<ConsoleParameter> Parameters { get; }
        public ConsoleEnvironment Environments { get; }

        public ConsoleCommand(string id, string name, string module, string description,
            IEnumerable<ConsoleParameter> parameters = null, IEnumerable<string> aliases = null,
            ConsoleEnvironment environments = ConsoleEnvironment.Development, IEnumerable<string> capabilities = null,
            IEnumerable<string> examples = null, string version = "1")
        {
            RequireIdentifier(id, nameof(id));
            RequireIdentifier(name, nameof(name));
            RequireIdentifier(module, nameof(module));
            var names = (aliases ?? Array.Empty<string>()).ToArray();
            foreach (string alias in names) RequireIdentifier(alias, nameof(aliases));
            if (names.Append(name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length + 1)
                throw new ArgumentException("Command names and aliases must be distinct.", nameof(aliases));
            var args = (parameters ?? Array.Empty<ConsoleParameter>()).ToArray();
            bool optionalSeen = false;
            var parameterNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? throw new ArgumentException("Null parameter.", nameof(parameters));
                if (!parameterNames.Add(arg.Name)) throw new ArgumentException("Duplicate parameter name.", nameof(parameters));
                if (optionalSeen && !arg.IsOptional) throw new ArgumentException("Required parameters must precede optional ones.", nameof(parameters));
                if (arg.IsVariadic && i != args.Length - 1) throw new ArgumentException("Variadic parameter must be last.", nameof(parameters));
                optionalSeen |= arg.IsOptional;
            }
            Id = id;
            Name = name;
            Module = module;
            Description = description ?? string.Empty;
            Version = version ?? string.Empty;
            Environments = environments;
            Aliases = new ReadOnlyCollection<string>(names);
            Parameters = new ReadOnlyCollection<ConsoleParameter>(args);
            Capabilities = new ReadOnlyCollection<string>((capabilities ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray());
            Examples = new ReadOnlyCollection<string>((examples ?? Array.Empty<string>()).ToArray());
        }

        private static void RequireIdentifier(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '"' || c == '\'' || c == '\\'))
                throw new ArgumentException("Identifiers must be nonempty and contain no whitespace, quotes or escapes.", parameter);
        }
    }

    public sealed class ConsoleArguments
    {
        private readonly object[] values;
        internal ConsoleArguments(object[] values) => this.values = values;
        public int Count => values.Length;
        public T Get<T>(int index) => (T)values[index];
        public object this[int index] => values[index];
    }

    public readonly struct ConsoleOutput
    {
        public string InvocationId { get; }
        public ConsoleOutputLevel Level { get; }
        public string Message { get; }
        public ConsoleOutput(string invocationId, ConsoleOutputLevel level, string message)
        { InvocationId = invocationId; Level = level; Message = message ?? string.Empty; }
    }

    public interface IConsoleOutput { void Write(ConsoleOutput output); }

    public readonly struct ConsoleOutputSnapshot
    {
        public long Revision { get; }
        public long ClearRevision { get; }
        public IReadOnlyList<ConsoleOutput> Entries { get; }
        internal ConsoleOutputSnapshot(long revision, long clearRevision, ConsoleOutput[] entries)
        { Revision = revision; ClearRevision = clearRevision; Entries = entries; }
    }

    public sealed class ConsoleInvocation
    {
        public string Id { get; }
        public ConsoleCaller Caller { get; }
        public ConsoleCommand Command { get; }
        private readonly IConsoleOutput output;
        internal ConsoleInvocation(string id, ConsoleCaller caller, ConsoleCommand command, IConsoleOutput output)
        { Id = id; Caller = caller; Command = command; this.output = output; }
        public void Write(string message, ConsoleOutputLevel level = ConsoleOutputLevel.Info) => output?.Write(new ConsoleOutput(Id, level, message));
    }

    public delegate Task<ConsoleResult> ConsoleHandler(ConsoleInvocation invocation, ConsoleArguments arguments, CancellationToken cancellation);
}
