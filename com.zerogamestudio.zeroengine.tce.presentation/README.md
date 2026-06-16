# ZeroEngine TCE Presentation

`com.zerogamestudio.zeroengine.tce.presentation` provides visual-only TCE presentation effects for renderer snapshots, afterimages, and soul ghost style echoes.

The package depends on `ZeroEngine.TCE` and UnityEngine renderer APIs. It does not own project gameplay rules, save state, inventory, combat semantics, pooling policy, or project assets. POB adapters and other project adapters should depend inward on this package and keep their domain behavior project-side.

## Public Surface

- `ITcePresentationSource` lets a project expose custom visual capture.
- `ITcePresentationPlayer` and `TcePresentationHandle` play and dispose captured visuals.
- `TceVisualSnapshot`, `TceMeshSnapshot`, `TceSpriteSnapshot`, and `TceSpriteLayerSnapshot` are the supported snapshot models.
- `TcePresentationPlaybackSettings` controls style, tint, duration, fade window, alpha curve, material override, sprite sorting override, renderer tint property name, mesh texture property name, offset, direction, and texture copy policy.
- `SpawnSnapshotEffectData` and `SpawnSoulGhostEffectData` are catalog-visible TCE effects.

## Quick Integration

1. Add the package through Unity Package Manager or as an embedded ZeroEngine package.
2. Reference `ZeroEngine.TCE.Presentation` from the consuming runtime assembly.
3. Use `TceRendererSnapshotSource` for generic Unity renderers, or implement `ITcePresentationSource` for project-specific renderers.
4. Execute `SpawnSnapshotEffectData` or `SpawnSoulGhostEffectData` from a TCE graph, or call `ITcePresentationPlayer.Play` from a project adapter.

## Graduation Checks

Before publishing or pinning this package, run the release gates in `Documentation~/release-gates.md`. The minimum local gate is:

```powershell
Unity EditMode assembly_names=ZeroEngine.TCE.Presentation.Tests.Editor
Unity EditMode assembly_names=ZeroEngine.TCE.Tests.Editor
```

Both Unity test runs must report `total > 0` and `failed = 0`.
