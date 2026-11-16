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

The registry reuses its sorted catalog until a module registers or unloads.
Discovery and completion still check the current caller and policy on every
request; returned command arrays remain caller-owned snapshots. Execution links
module/session cancellation with an optional caller token and creates a deadline
timer only when requested. Cancelling an uncooperative handler keeps the session
busy until that handler ends, without replaying it or claiming rollback.

Built-in scalar conversion includes `byte`, `sbyte`, `short`, `ushort` and `char`
alongside the larger integer, floating-point, decimal, bool, enum, string and Guid
types. Integer bounds are enforced; `char` requires one UTF-16 code unit. Nullable
and array conversion reuse the same rules, with explicit custom converters taking
precedence for their registration lifetime.

`ConsoleHost` also supplies `Vector2/3/4`, `Vector2Int/3Int`, `Quaternion`, `Color`
and `Color32` conversion. Use comma-separated components, optionally enclosed in
parentheses or brackets; vector/quaternion component counts must match the type.
Colors accept normalized RGB/RGBA components, HTML/named colors and `0xRRGGBB[AA]`.
Nonfinite components, integer overflow and colors outside `[0,1]` are rejected.
These rules also compose with nullable values and arrays.

The pure core keeps its parameterless `ConsoleValues` constructor and engine
independence. `new ConsoleValues(fallback)` supplies an explicit conversion
fallback after built-ins; return false with no error for an unclaimed type.
Type-specific `Register` converters take precedence and disposing their handle
restores the default behavior. No value or scene-object cache is created.

Standard generic collections also accept `[item, item]`, including nested values.
List/collection interfaces use `List<T>`, set interfaces use `HashSet<T>`; queue
and stack construction follows their normal enumerable constructors. Dictionaries
use `[(key, value), (key, value)]` and reject duplicate or null keys. Explicit
converters and the existing fallback retain priority over these collection rules.

Numeric arithmetic is opt-in through `session.Values.EnableArithmetic = true`.
It supports `+ - * / %`, unary signs and parentheses, with bounded depth/work,
invariant literals and checked numeric ranges. Integer division truncates at each
division. Enclose an expression containing spaces in parentheses or quotes so it
remains one command argument. It does not evaluate C#, functions or object access.

`session.Macros = new ConsoleMacros()` opts that session into explicit `#name`
substitution. `Define`, `Remove`, `Clear` and `Snapshot` belong to its owner;
expansion is bounded and cycles fail before any handler runs. History keeps the
typed line, and expanded commands still pass the normal parser and live policy.
Unknown names remain unchanged. Macro-aware caret hints/completion are not
provided; use ordinary command text when requesting parameter guidance.

`ConsoleMethodBinding.Create` explicitly binds one selected `MethodInfo` and an
optional instance. It maps optional/params parameters, injects `CancellationToken`
and awaits `Task`/`Task<T>`. A returned `ConsoleResult` preserves its business
status; ordinary values become data, while void/untyped tasks remain unconfirmed.
Async void, custom awaitables, open generics and ref/out/pointer parameters require
explicit adapters. There is no automatic assembly scan or overload resolver.
Reflection bindings and dynamically constructed generic collections require the
consumer's normal stripping/AOT preservation; these additions have Editor coverage,
not a new IL2CPP support claim.

Results are immutable. Empty successes may be shared; use the invocation ID on
the context/output to identify a call, and read status/code/data from the result.
Session disposal cancels its active invocation and pending completion separately,
so execution does not need a second per-call session cancellation registration.

`ConsoleCommand.Signature` caches readable parameter types, optional defaults and
variadic markers. `session.GetHint(line, caret, caller)` returns the current
parameter and description without executing anything. Completion responses carry
the same metadata in `Hint`; both paths apply the caller's live policy. Use
`Apply(line, index, out caret)` when editing the middle of a line so the insertion
caret stays before the retained tail. Cached completion metadata is unfiltered;
caller permissions are checked for each request and again after async providers.

Unity hosts format data-only results by default. An explicit result message takes
precedence, and the original status/data remain unchanged. Bare core sessions
keep presentation opt-in: set `ResultFormatter = ConsoleValueFormatter.Default`
or supply `IConsoleValueFormatter`. Set it to `null` for data-only automation.
The default formats invariant scalars, quoted collection strings, dictionaries,
tuples and nested sequences, with depth/item/length limits and cycle detection.
It does not inspect arbitrary properties or call unknown objects' `ToString`.
Override `TryFormatAtom` for explicit application types; Unity values stay in
`UnityConsoleValueFormatter`. Formatting errors do not turn a completed business
operation into a failure or execute it again.
Custom formatters may run on the completion thread; restrict them to safe data
formatting and dispatch scene-object work explicitly through the Unity owner.

For frequent output refreshes, use `CopyOutputTo(ref buffer, out revision)` with
a caller-owned `ConsoleOutput[]`. It grows the buffer when needed, returns the
valid entry count and captures the revision under the same lock. Unused entries
are cleared. Reuse that buffer on one consuming thread; `GetOutput` and
`GetOutputSnapshot` remain independent allocating snapshots for occasional reads.

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

Kernel EditMode cases cover binding, live policy, registration lifetime and
cancellation. Host PlayMode cases cover Unity dispatch and lifecycle. Native
UI/QC compatibility, Player behavior and consumer adoption have separate checks.
