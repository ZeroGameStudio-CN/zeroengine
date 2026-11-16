# ZE Console UI

Add `ConsoleHost` and `NativeConsole` to an owned
GameObject. The view builds its own bounded hierarchy, uses the host's native
pipeline and can operate without Quantum Console. A project can supply a font
and an input-isolation lease; the view restores its own cursor/selection state.

Fonts are resolved lazily when the view opens. Without a project font, one dynamic
OS font uses an ordered family list for Latin, CJK and other installed scripts,
falling back to Unity's built-in font. Unity selects an available face per glyph;
the package includes no font files or baked atlas. Reopening reuses the same font.

Assign the Inspector **Font**, or call `ConfigureFont(projectFont)`, to borrow the
project's existing `UnityEngine.Font` unchanged, including its own fallback rules.
This never rewrites font names, materials, import settings, TMP assets or global
fallback tables, and never destroys the borrowed font. Font changes update only
the console's labels without rebuilding its view or clearing draft/history.
`ConfigureFont(null)` restores automatic OS selection. The third-party
`ConsoleInputOverlay` exposes the same configuration for its own labels and leaves
the external input widget untouched.

For project localization, set `FontProvider` once to return the active source
`UnityEngine.Font`; it is queried once per open, not every frame. A null result or
provider failure retains the current valid font; an unloaded font falls back on
reopen. Keep the project's font lease while the console is open. A locale-change
event can call `ConfigureFont` directly to update an already open view.

OS coverage varies by device. A supplied bitmap font keeps its fixed glyph set;
platforms without accessible OS fonts, such as WebGL, need a project-supplied font
with the required glyphs. No bundled universal font means no guarantee of every
Unicode character. Legacy uGUI glyph fallback does not add complex-script shaping,
bidirectional layout or color emoji support. Target-platform rendering and input
still require their matching native checks; Editor coverage does not certify them.

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

The desktop window opens at the lower left, defaulting to 880 × 480 reference
units. Adjust **Preferred Size** in the Inspector or `NativeConsole.PreferredSize`
at runtime; it fits inside the available Canvas and soft-keyboard area. The
toolbar supplies Clear, Copy, font size (12–28) and an explicit
follow-output switch. Turning follow off keeps the current reading position
when messages arrive; turning it on returns to the newest output. The scrollable
view retains up to the last 256 session entries and displays each message up to
the session's own limit, without an additional 1,024-character UI truncation.

Use Up/Down for history, Tab to insert a completion, Enter to execute, and Escape
to close. Choose other completions with their rows or arrow buttons. Touch layouts
hide the keyboard-shortcut footer.
Completion selection never executes a command. Its visible rows adjust to the
window and font size; submitting clears the stale suggestions. Projects that
own the toggle action retain it through `KeyboardShortcutEnabled = false`.
An empty or whitespace-only draft keeps the log view clear; suggestions appear
after text is entered. Clearing the draft also cancels pending suggestions.

Resolved commands show a compact signature and the active parameter's type and
description above the input. Optional defaults and variadic parameters stay in
the signature; quoted arguments and a caret in the middle select the correct
parameter. The hint uses the same policy as completion, reserves its own area
above the input, and releases that space when the draft clears. Applying a
completion retains the remaining text and puts the caret after the inserted value.
Data-only command returns use the shared Host formatter; existing result messages
remain authoritative and business status is preserved.

Common actions use a consistent set of procedural line icons, with Chinese
descriptions and shortcut hints on hover. The follow control shows its active or
paused state; ready/running and cancellation retain distinct visual states.
Hints stay inside the window and do not take keyboard focus. Icons require no
sprite sheet, icon font or external UI package.

The visible title is **ZE Console**. Graphite translucent surfaces and restrained
blue-gray accents retain contrast without a blur pass or additional texture.
`TouchLayoutPreview` previews the mobile canvas scale in the Editor; mobile builds
select it automatically, choose landscape or portrait scaling from the actual
screen dimensions, and account for the reported soft-keyboard height. The package
uses the project's screen orientation settings.

## Shared access

Add `ConsoleAccessController` on the same owner to reuse Editor access and hidden
release activation. Editor/development builds open directly; release builds stay
hidden until the configured text sequence, seven upper-left corner taps, or a
project's existing hidden path calls `TryUnlock` / `Unlock`. Configure the text
sequence and tap count once; zero taps disables the gesture. There is no shared
default password or always-visible release button.

`Lock` revokes access for this session. Unlocking initializes the host; `Toggle`
opens the view. Command environments and capabilities still
apply. Existing project unlock paths can call this component instead of
reimplementing its gate. Supply the existing input-isolation lease where needed.
Touch/soft-keyboard behavior requires device acceptance; an Editor preview alone
does not establish Android or iOS support.

The search icon opens a collapsible log-search row. Search matches committed text
as a literal, case-insensitive substring, including Chinese; it does not run
commands or change the command draft, history or retained output. New messages
use the current filter. An empty result has an explicit hint. Copy exports the
matching retained messages while a filter is active; clearing or closing search
restores the full output and Copy behavior. The displayed pool remains bounded
to the last 256 matching entries and is reused between filter changes.
Output refreshes reuse a caller-owned session buffer and the existing text rows;
they do not allocate a new snapshot array for every batch of messages. Hidden rows
release their old text after filtering or clearing.
`GetFilteredOutputText()` returns the same plain text without touching the OS
clipboard, for project-owned exports and headless verification. Toolbar actions
preserve the selected input field; explicitly running a command selects its draft.

Composition guards read the current EventSystem module's active `BaseInput`,
including its `inputOverride`, before falling back to Unity's legacy input.
Custom input providers must expose their current `compositionString` there.
Native and Quantum use the same guard and defer submission while that provider
reports an active composition.

## Dependencies

The native view requires this repository's Console host and Unity's uGUI/UI
modules. It does not depend on Quantum Console, TextMeshPro, UniTask, a tweening
library, an icon set or additional font assets. Rounded surfaces are small UI
meshes; their materials and rendering remain Unity UI's defaults.

The optional `ZeroEngine.Console.UI.InputSystem` assembly is compiled only when
Unity Input System is installed and enabled. It registers keyboard, EventSystem
and composition input through `ConsoleInputBackend`; the native UI assembly has
no reference to `Unity.InputSystem`. Without the adapter, the existing legacy
input path remains available. New-input-only projects need that official Unity
package; this package does not install it automatically.
