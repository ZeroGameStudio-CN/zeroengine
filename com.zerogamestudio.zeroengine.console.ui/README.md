# ZE native Console UI

G4 vertical-slice candidate. Add `ConsoleHost` and `NativeConsole` to an owned
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

Both presentations use the same host, policy, quoted argument completion,
bounded output/history and cancellation. Switching closes the previous view
and releases its EventSystem/input state before opening the next; it does not
cancel or replay an already started business command.

Composition guards read the current EventSystem module's active `BaseInput`,
including its `inputOverride`, before falling back to Unity's legacy input.
Custom input providers must expose their current `compositionString` there.
Native and Quantum use the same guard and defer submission while that provider
reports an active composition.
