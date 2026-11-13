# Native Console integration

Read the installed Console README before choosing assemblies. The pure
`ZeroEngine.Console.Core` owns descriptors, parsing, policy and sessions.
`ZeroEngine.Console.Unity` supplies main-thread hosting; native uGUI and the
optional Quantum adapter are separate packages. A project owns its command
module and gameplay handlers. The vendor SDK is not a business-command registry.

Register explicit `ConsoleBinding` values and retain the returned disposable
module handle. Use stable command IDs and names, typed parameter declarations,
environment/capability policy and the original business guard. Both text and
typed execution traverse the same session. Dispose registrations and owned
subscriptions with their module/scene lifetime; do not scan all assemblies or
compile command code at Player startup.

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
scrollback, font size and follow-output controls. Up/Down recalls history;
Page Up/Page Down selects completion candidates across pages and Tab inserts
the selected value. Selection does not execute a handler. Keep the current
Quantum entry and command inventory usable while adopting Native explicitly;
frontend layout changes are not permission to remove project commands or switch
production defaults.
