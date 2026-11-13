# ZE native Console UI

Native frontend candidate. Add `ConsoleHost` and `NativeConsole` to an owned
GameObject. The view builds its own bounded hierarchy, uses the host's native
pipeline and can operate without Quantum Console. A project can supply a font
and an input-isolation lease; the view restores its own cursor/selection state.

The default desktop font prefers an installed CJK family, with Unity's built-in
font as a fallback. Projects targeting platforms without a suitable OS font must
assign their own font. Target-platform input and font support require the matching
native checks; source presence does not certify them.

Use `ConsoleFrontendSelector` on the same owner for `Auto / Native / Quantum`
selection. Supply an optional frontend component using `ConfigureQuantum` and
open through the selector. With no compatible provider, Auto uses Native and
records one reason. `AcquireInputIsolation` supplies the project's disposable
gameplay/navigation lease and is restored when the selector is disabled.
Set `Preference` before enabling an inactive owner to choose Native without an
unnecessary missing-Quantum warning. Set `KeyboardShortcutEnabled = false` on
the selector when the project's existing input action owns opening and closing.

Both presentations use the same host, policy, quoted argument completion,
bounded output/history and cancellation. Switching closes the previous view
and releases its EventSystem/input state before opening the next; it does not
cancel or replay an already started business command.

The native desktop window opens at the lower left. Its bounded 760 × 440
reference layout fits inside the current Canvas and leaves the remaining game
view visible. The toolbar supplies Clear, Copy, font size (12–28) and an explicit
follow-output switch. Turning follow off keeps the current reading position
when messages arrive; turning it on returns to the newest output. The scrollable
view retains up to the last 256 session entries and displays each message up to
the session's own limit, without an additional 1,024-character UI truncation.

Use Up/Down for history, Page Up/Page Down (or the completion arrows) to select
across completion pages, Tab to insert the selected value, and Escape to close.
Completion selection never executes a command. Its visible rows adjust to the
window and font size; submitting clears the stale suggestions. Projects that
own the toggle action retain it through `KeyboardShortcutEnabled = false`.

Composition guards read the current EventSystem module's active `BaseInput`,
including its `inputOverride`, before falling back to Unity's legacy input.
Custom input providers must expose their current `compositionString` there.
Native and Quantum use the same guard and defer submission while that provider
reports an active composition.
