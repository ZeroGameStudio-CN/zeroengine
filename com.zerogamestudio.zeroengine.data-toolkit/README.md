# ZeroEngine.DataToolkit

Reusable Unity editor tooling for discovering, browsing, inspecting, and
validating project data assets.

## Use Cases

- Browse ScriptableObject data collections from a single editor window.
- Automatically discover explicit `ManageableData`, `CreateAssetMenu`, and
  ZeroEngine-style config ScriptableObject types.
- Inspect large data assets without forcing expensive full inspector rendering.
- Validate stable IDs, duplicate IDs, and broken object references before play
  mode or build time.
- Catch common designer config mistakes such as empty display text, negative
  non-negative fields, out-of-range probabilities, invalid Min/Max pairs, and
  empty object-reference list entries.
- Attach package-specific validators to the same validation report without
  coupling DataToolkit to gameplay packages.
- Export validation summaries to CSV for designer review and production signoff.
- Preview and apply field-level CSV imports with Undo support and per-row error
  reporting.
- Batch edit one serialized field across the visible assets in the selected
  type, with a dry-run preview before applying.
- Inspect outgoing and incoming object references for selected data assets.
- Add project-specific actions and footers around reusable data views.
- Keep designer-facing data workflows outside game-specific editor code.

## Installation

Add the package through Unity Package Manager:

```text
https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.data-toolkit#<tested-commit>
```

Use a tested commit hash for production projects.

## Requirements

- Unity 2022.3 or newer.
- Optional Odin/Sirenix integrations are used only when a downstream project
  has those assemblies available.

## Notes For Maintainers

This package is intentionally editor-only. Keep runtime dependencies out of the
package unless the data browsing workflow cannot work without them.

The generic validation rules are intentionally conservative. Package-specific
validators should implement `IDataToolkitValidationProvider` and be registered
through `DataToolkitProjectProfile`. The generic default profile also discovers
parameterless `IDataToolkitValidationProvider` implementations in loaded editor
assemblies, so packages can contribute validators without hard-coding gameplay
package references into this editor package. For ZeroEngine/ZGS packages that
already expose a static `*ConfigValidator.Validate(IEnumerable<TConfig>...)`
entry point, DataToolkit adapts those validators into the shared report at
runtime without requiring the package to reference `ZGS.DataToolkit.Editor`.

Field import CSV files use `Path`, `Field`, and `Value` columns. `AssetPath`,
`FieldPath`, and `NewValue` are accepted aliases. The importer currently targets
single-line CSV rows and supports string, integer, boolean, float, and enum
serialized fields.
