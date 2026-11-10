# ZE native Console — G4 protocol and vertical slice

The [G3 package baseline](../packages/2026-11-10-g3-acceptance.md) is frozen at
`8441097`. This stage implements one native execution pipeline before migrating
the project commands. Production project dependencies and Quantum assets keep
their current owners. Source implementation is not consumer adoption or release.

## Package boundaries

| Package | Responsibility |
| --- | --- |
| `com.zerogamestudio.zeroengine.console` | Pure C# descriptors, registration, parsing, binding, policy, execution, completion and bounded session state; a separate Unity assembly supplies main-thread dispatch and lifecycle hosting. No dependency on QC or gameplay packages. |
| `com.zerogamestudio.zeroengine.console.ui` | Independently usable native lightweight uGUI frontend, history, output and completion presentation. Project font/input isolation can be supplied explicitly. |
| `com.zerogamestudio.zeroengine.console.quantum` | Optional verified QC frontend. Uses public SDK APIs, owns its UI instance and forwards raw input to the native pipeline. Vendor code/assets do not enter ZE. |

Bare gameplay packages remain installable without Console. Additional module
commands use explicit optional composition rather than changing every module's
dependencies. Project resource IDs, singletons and business rules remain project
code. The AI Support package follows G7 after the catalog and API have stabilized.

## Core contract

- Descriptors carry a stable ID, primary name, aliases, module/source identity,
  help/examples, parameter schema, environment/capability policy and version.
  Registry publication is atomic per module. Duplicate IDs or names reject the
  whole registration; disposal removes discovery and cancels owned invocations.
- An explicit handler receives typed arguments, a caller context, output sink
  and cancellation. Both direct invocation and command lines traverse the same
  policy and argument binding. The kernel has no Unity, QC or project references.
- Parsing preserves source spans, empty quoted arguments, Unicode, escaped
  quotes, negative numbers and bracketed values. Conversion is invariant-culture;
  native/project converters own engine values, enums and resource identifiers.
  Invalid syntax/arity/conversion has a stable error code and argument location.
- Results distinguish success, failure, cancellation and deadline expiry, with
  optional data and bounded output events. A `void` legacy handler is not given a
  fabricated business-success result. Cancelling never claims rollback of an
  already applied side effect or replays a command after frontend failure.
- Listings and completion apply the same policy as execution. Completion returns
  stable candidate identities and source replacement ranges. Dynamic providers
  support cancellation/invalidation; no whole-project asset scan per keystroke.
- History, output, pending requests and completion have explicit capacity limits.
  Unity hosting dispatches engine work on the main thread and releases input,
  subscriptions and module handles on close/disable/unload.

Registration is explicit and AOT-compatible. Optional authoring attributes or
Editor-generated registries must produce repeatable static calls before a Player
build; Player startup does not scan all assemblies or compile dynamic code.

## Quantum feasibility boundary

Installed-source inspection confirms that `OnInvoke` fires after QC execution.
The public response API can deliver raw input before QC parsing, but response
mode suppresses QC's own suggestion UI and has no public general end-response
operation in the inspected installation. Therefore a response bridge must own
its cloned QC instance, rearm through public lifecycle callbacks and supply native
completion presentation; it must not leave borrowed QC instances in response mode.

`Auto / Native / Quantum` selects a single active frontend and input lease. Auto
uses QC only with a verified compatibility profile and usable presentation;
otherwise it selects Native and emits one specific reason. Unknown SDK versions
are not assumed compatible. Physical SDK absence, uninstall, callback disposal,
quotes, async output and no-double-execution are required slice checks.
Reflection, if needed for optional SDK isolation, is restricted to public members
with explicit preservation/verification. No private-field patching, vendor-source
changes or fake vendor types are allowed.

## Compatibility inventory and slice exit

The current POB catalog has 52 contracts with 157 distinct aliases, plus the
separate `pob-death-blood` command: 158 total. Read-only extraction uses the
project's actual catalog parser and frozen source hashes, yielding 99 distinct
parameter strings. This is a signature baseline, not yet the complete migration
decision for policy, completion, side effects and output.

G4 first proves a read-only query, a project-owned write, dynamic completion and
an asynchronous/cancellable operation through the same pipeline. Native must run
with no QC installation. QC either passes the same checks or receives a concrete
compatibility limitation before broader migration. Check domain reload, duplicate
opening, module disposal, focus/input restoration and the declared AOT path.
Record a comparable QC/Native baseline before making cost or superiority claims.

Only after that vertical contract is verified does G5 migrate all inventory
entries, followed by G6 experience/cleanup and G7 synchronized Skill delivery.
The immutable package baseline remains independently reviewable in its own PR.
