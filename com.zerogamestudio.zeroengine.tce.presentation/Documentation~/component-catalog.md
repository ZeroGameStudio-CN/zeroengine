# ZeroEngine TCE Presentation Component Catalog

Package status: `0.2.0` release-hardened baseline.

This package is visual-only. Presentation effects capture renderer state and play temporary visuals. They do not apply gameplay damage, spawn gameplay actors, alter stats, or dispatch project events.

## Effect

### Spawn Snapshot

- Component ID: `zeroengine.tce.presentation.effect.spawn_snapshot`
- Compatibility: stable component ID for `0.2.0`.
- Summary: Captures and plays a visual-only snapshot of the resolved target.
- Runtime type: `ZeroEngine.TCE.Presentation.SpawnSnapshotEffect`
- Data type: `ZeroEngine.TCE.Presentation.SpawnSnapshotEffectData`

Fields:

- `Target`: Target actor selection inherited from TCE effects.
- `Settings`: Visual playback settings including style, tint, duration, fade delay, fade duration, alpha curve, material override, sprite sorting override, sprite renderer color control, offset, direction, renderer tint property name, sprite/mesh main texture property name, and main texture copy policy.

### Spawn Soul Ghost

- Component ID: `zeroengine.tce.presentation.effect.spawn_soul_ghost`
- Compatibility: stable component ID for `0.2.0`.
- Summary: Captures and plays a soul-ghost style visual snapshot of the resolved target.
- Runtime type: `ZeroEngine.TCE.Presentation.SpawnSoulGhostEffect`
- Data type: `ZeroEngine.TCE.Presentation.SpawnSoulGhostEffectData`

Fields:

- `Target`: Target actor selection inherited from TCE effects.
- `Settings`: Visual playback settings using `SoulGhost` style by default. The same fade, alpha curve, material, sorting, offset, direction, and texture-copy options are available.
