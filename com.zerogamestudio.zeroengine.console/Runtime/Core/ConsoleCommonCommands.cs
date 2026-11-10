using System;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroEngine.Console
{
    public static class ConsoleCommonCommands
    {
        public static IDisposable Register(ConsoleSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            const string module = "ze.console";
            return session.Registry.Register(module, new[]
            {
                new ConsoleBinding(new ConsoleCommand("ze.console.help", "ze-help", module,
                    "List available commands or filter their names and descriptions.",
                    new[] { new ConsoleParameter("filter", typeof(string), optional: true, defaultValue: "") },
                    environments: ConsoleEnvironment.All, examples: new[] { "ze-help", "ze-help session" }),
                    (context, args, token) =>
                    {
                        string filter = args.Get<string>(0) ?? string.Empty;
                        var commands = session.GetCommands(context.Caller).Where(command =>
                            command.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            command.Description.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            command.Aliases.Any(alias => alias.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
                        foreach (var command in commands)
                        {
                            string parameters = string.Join(" ", command.Parameters.Select(parameter =>
                                (parameter.IsOptional ? "[" : "<") + parameter.Name + (parameter.IsVariadic ? "..." : "") +
                                (parameter.IsOptional ? "]" : ">")));
                            context.Write(command.Name + (parameters.Length == 0 ? "" : " " + parameters) + " — " + command.Description);
                        }
                        return Task.FromResult(ConsoleResult.Success(commands.Length + " commands", commands));
                    }),
                new ConsoleBinding(new ConsoleCommand("ze.console.clear", "ze-clear", module,
                    "Clear the bounded session output.", environments: ConsoleEnvironment.All),
                    (context, args, token) => { session.ClearOutput(); return Task.FromResult(ConsoleResult.Success()); }),
                new ConsoleBinding(new ConsoleCommand("ze.console.session", "ze-session", module,
                    "Read this console session's environment and installed command modules.", environments: ConsoleEnvironment.All),
                    (context, args, token) =>
                    {
                        string[] modules = session.GetCommands(context.Caller).Select(command => command.Module).Distinct(StringComparer.Ordinal)
                            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
                        context.Write("environment=" + context.Caller.Environment + "; caller=" + context.Caller.Identity);
                        context.Write("modules=" + string.Join(", ", modules));
                        return Task.FromResult(ConsoleResult.Success(data: modules));
                    })
            });
        }
    }
}
