# ZeroEngine TCE Presentation API Compatibility

This document defines the `0.2.0` compatibility baseline for `com.zerogamestudio.zeroengine.tce.presentation`.

## Stable Public Surface

These runtime contracts are stable for downstream adapters:

- `ITcePresentationSource`
- `ITcePresentationPlayer`
- `TcePresentationHandle`
- `TceVisualSnapshot`
- `TceMeshSnapshot`
- `TceSpriteSnapshot`
- `TceSpriteLayerSnapshot`
- `TceVisualSnapshotRequest`
- `TcePresentationPlaybackSettings`

The `TcePresentationPlaybackSettings` contract is additive for `0.2.0` consumers. The default playback semantics remain total-duration linear alpha fade, package fallback material, captured sprite sorting, `_Color` renderer tint with SpriteRenderer color enabled, and `_MainTex` sprite/mesh texture playback. Optional fields may override fade timing, alpha curve, material, sprite sorting, SpriteRenderer color writes, RGB tint fade, renderer tint property name, and main texture property name without changing those defaults.

The `TcePresentationStyle` numeric values are stable:

- `StaticSnapshot = 0`
- `MeshSnapshot = 1`
- `LayeredSpriteSnapshot = 2`
- `SoulGhost = 3`

These TCE component IDs are stable:

- `zeroengine.tce.presentation.effect.spawn_snapshot`
- `zeroengine.tce.presentation.effect.spawn_soul_ghost`

## Authored projections in 0.4.0

The development-stage authored-graph API is revised directly in `0.4.0`.
`TceVisualPrefab.TryCreate` accepts `TceVisualProjectionPolicy`; the former
source-only delegate overload is removed. Adapters use the policy to classify
source producers, pure rendering data and generated meshes.

`TceVisualDynamics` captures resolved visual controls and samples event-free
display clips. `TryGetParticlePlayback` resolves Timeline-driven particle clocks
that Unity reports as manually simulated. `TceVisualFrame` carries bounded
visual state; the consumer retains transport, identity, budgets and lifetime.
These authored-projection APIs are outside the snapshot baseline above.

## Evolvable Surface

These areas may evolve in minor versions while preserving the stable contracts above:

- internal `TcePresentationRunner` update and cleanup implementation;
- sample README content;
- additional optional snapshot sources;
- additional presentation-only effect data types.

## Breaking Change Policy

After `0.2.0`, these changes require a migration or a major version bump:

- changing a stable interface signature;
- changing a stable enum numeric value;
- changing a stable component ID;
- renaming public serialized fields on catalog-visible effect data;
- removing a stable snapshot type without a deprecation path.

Breaking changes must be documented before implementation and covered by package tests.
