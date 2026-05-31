# ZeroEngine.DataToolkit

Reusable Unity editor tooling for discovering, browsing, inspecting, and
validating project data assets.

## Use Cases

- Browse ScriptableObject data collections from a single editor window.
- Inspect large data assets without forcing expensive full inspector rendering.
- Add project-specific actions and footers around reusable data views.
- Keep designer-facing data workflows outside game-specific editor code.

## Installation

Add the package through Unity Package Manager:

```text
https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.data-toolkit#main
```

In production, pin a tested commit hash instead of `#main`.

## Requirements

- Unity 2022.3 or newer.
- Optional Odin/Sirenix integrations are used only when a downstream project
  has those assemblies available.

## Notes For Maintainers

This package is intentionally editor-only. Keep runtime dependencies out of the
package unless the data browsing workflow cannot work without them.
