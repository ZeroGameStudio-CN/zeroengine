# ZE Console integration

Read the installed Console README before choosing assemblies. The pure
`ZeroEngine.Console.Core` owns descriptors, parsing, policy and sessions.
`ZeroEngine.Console.Unity` supplies main-thread hosting; native uGUI and the
optional Quantum adapter are separate packages. A project owns its command
module and gameplay handlers. The vendor SDK is not a business-command registry.

The default host registers only `ze-help`, `ze-clear` and `ze-session`. Register
feature and project commands explicitly; keep game-specific operations in the
consumer project and avoid importing a vendor's complete extras catalog.

Frequent log consumers should reuse `CopyOutputTo(ref buffer, out revision)`;
only the returned prefix is valid and unused entries are cleared. Keep each
buffer with one consumer. Snapshot APIs remain available for occasional exports.

Register explicit `ConsoleBinding` values and retain the returned disposable
module handle. Use stable command IDs and names, typed parameter declarations,
environment/capability policy and the original business guard. Both text and
typed execution traverse the same session. Dispose registrations and owned
subscriptions with their module/scene lifetime; do not scan all assemblies or
compile command code at Player startup.

Catalog caching must preserve live caller/policy checks; never cache authorized
command lists across callers or unlock changes. Keep deadline timers optional.
Use the Host's standard Unity value conversions before adding project parsers.
The pure core accepts an explicit fallback; scoped type converters override it.
Standard collections use bracket syntax; dictionary entries are `(key, value)`
pairs. Preserve converter/fallback priority. Arithmetic and per-session `#name`
macros require explicit opt-in; keep their bounded parsing and live policy checks.
Macros affect execution, not caret hints/completion. Explicit `ConsoleMethodBinding`
can bind a selected method with defaults, params and Task results; it does not scan
assemblies or resolve overloads. Async void/custom awaitables need an outcome
adapter. Preserve reflected methods and generic collection instantiations for
stripping/AOT; Editor checks alone do not establish IL2CPP support.

Use completion `Hint` or policy-checked `GetHint` for parameter guidance; never
execute a command to discover its signature. Apply the returned insertion caret
when retaining text after a completion. Unity hosts format data-only returns;
an explicit message takes precedence. Bare sessions opt in through
`ResultFormatter`; keep status/data intact and use bounded explicit formatters
instead of property reflection or scene-object inspection.

Only a handler with an established business outcome returns `Success`.
`Unconfirmed` preserves a legacy void/string result without claiming success;
`Started` means completion is unobserved. Neither makes `IsSuccess` true.
Cancellation is distinct from rollback and must not replay an applied action.
Host cancellation before main-thread dispatch returns `Cancelled` without
running the handler. Release hosts remain disabled by default.

Completion returns canonical insertion values separately from readable labels,
with explicit source replacement spans. Apply the returned completion instead
of concatenating a display string. Use bounded, cancellable providers and lazy
indexes; an unclaimed parameter may return `null` for built-in enum/bool/nullable
completion, while an empty list deliberately supplies nothing.

Keep one active frontend and one input owner. Native and verified Quantum use
the same session and handlers. Unknown SDK identity or unusable presentation
selects Native with a reason; a fallback does not establish QC support. Use
project-owned presentation roots and source-verified compatibility profiles.
Verify actual rendered text, submission, pause/cancel and input restoration
through the project's authorized route. Read the installed adapter's exact
Player support boundary; Editor/Mono evidence cannot certify IL2CPP or physical
system IME behavior.

Native presentation opens at the lower left and supplies clear/copy, bounded
scrollback, font size and follow-output controls. Up/Down recalls history, Tab
inserts a completion, Enter executes and Escape closes. Choose other completions
with their rows or arrow buttons; touch layouts hide keyboard hints. Completion
selection does not execute a handler. Keep the current
Quantum entry and command inventory usable while adopting Native explicitly;
frontend layout changes are not permission to remove project commands or switch
production defaults.
Empty drafts keep logs visible; completion is requested only after input begins.

Native fonts default to a reused dynamic OS family chain with no bundled font
files. Preserve a project's font via the Inspector or `ConfigureFont(Font)`;
`FontProvider` can return its active localization font once per open. Borrowed
fonts/materials and shared TMP/global fallback assets must remain unchanged and
project-owned. Call `ConfigureFont` on a locale-change event for a live view;
null resets OS selection. The input overlay has the same local configuration.
Keep project font leases while visible. OS fallback is device-dependent, bitmap
fonts keep their existing glyph set, and legacy uGUI does not gain complex-script
shaping or RTL layout from adding fallback families. Do not advertise universal
language/device support or bundle a second project font collection by default.
