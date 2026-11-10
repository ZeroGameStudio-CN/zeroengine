# ZE native Console UI

G4 vertical-slice candidate. Add `ConsoleHost` and `NativeConsole` to an owned
GameObject. The view builds its own bounded hierarchy, uses the host's native
pipeline and can operate without Quantum Console. A project can supply a font
and an input-isolation lease; the view restores its own cursor/selection state.

The default desktop font prefers an installed CJK family, with Unity's built-in
font as a fallback. Projects targeting platforms without a suitable OS font must
assign their own font. Target-platform input and font support require the matching
native checks; source presence does not certify them.
