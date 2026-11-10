# ZeroEngine.Console

G4 implementation candidate. This package owns native command contracts and a
frontend-independent execution pipeline. Projects explicitly register modules;
gameplay packages do not acquire a Console dependency.

The pure `ZeroEngine.Console.Core` assembly references neither Unity nor Quantum
Console. Unity hosting and frontends are separate boundaries. See the
[protocol specification](https://github.com/ZeroGameStudio-CN/zeroengine/blob/codex/ze-native-console-20261110/docs/specs/2026-11-10-native-console.md) for the slice
and migration gates. No consumer adoption or release is established by this source.

```csharp
using ZeroEngine.Console;
using System.Threading.Tasks;

var registry = new ConsoleRegistry();
var session = new ConsoleSession(registry);
var registration = registry.Register("project", new[] {
    new ConsoleBinding(
        new ConsoleCommand("project.echo", "project-echo", "project", "Echo a value.",
            new[] { new ConsoleParameter("value", typeof(string)) }),
        (context, args, token) => Task.FromResult(ConsoleResult.Success(args.Get<string>(0))))
});
var result = await session.ExecuteLineAsync("project-echo \"中文 value\"",
    new ConsoleCaller(ConsoleEnvironment.Editor));
```

Dispose registrations when a module unloads, then dispose the session and registry
when their owner ends. A Unity owner can use `ConsoleHost`; its dispatcher keeps
handlers on the main thread, including calls submitted from a worker.

The first candidate has 22 portable/native kernel cases and three native host
lifecycle cases. Native UI/QC compatibility and project migration have separate
gates; these counts do not establish Player or consumer adoption.
