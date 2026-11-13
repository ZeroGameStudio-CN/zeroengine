# ZeroEngine.Console

Implementation candidate. This package owns native command contracts and a
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

Legacy wrappers can return `ConsoleResult.Unconfirmed` after a `void` or string
handler returns, or `ConsoleResult.Started` for work whose completion cannot be
observed. Both preserve optional data, report warning output and keep `IsSuccess`
false. Only a handler with an established business outcome returns `Success`.
A custom completer may return `null` for a parameter it does not own; the session
then supplies its built-in bool/enum completion, including nullable values and
the explicit `null` token. An empty list deliberately supplies no candidates.
Command names and aliases match any case-insensitive fragment, with prefix
matches shown first. Parameter completion searches the canonical value and its
display label; applying a candidate inserts the canonical value with native quoting. Editor resource
providers can therefore offer metadata-only asset paths under readable labels.
`ConsoleHost.ExecuteAsync(idOrName, object[])` provides the same main-thread,
policy-checked path for typed callers. Projects that rely on legacy `Debug.Log`
feedback can explicitly enable `CaptureUnityLogs`; messages use the session's
bounded queue and the subscription ends when the host is disabled. Capture is
off by default and does not turn log text into a business result.

Both Host execution overloads return a structured `Cancelled` result when the
request token is cancelled before main-thread dispatch. The handler is not run;
this applies to requests made on the Unity thread and on worker threads.

The current focused Unity 6 evidence covers 27 kernel cases and five Host cases.
Native UI/QC compatibility and project migration retain their separate gates;
these counts do not establish Player or consumer adoption.
