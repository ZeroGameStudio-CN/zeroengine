# ZeroEngine TCE Presentation

`com.zerogamestudio.zeroengine.tce.presentation` provides visual-only TCE presentation effects for renderer snapshots, afterimages, and soul ghost style echoes.

The package depends on `ZeroEngine.TCE`, Unity Timeline 6.6 and UnityEngine renderer APIs. It does not own project gameplay rules, save state, inventory, combat semantics, pooling policy, or project assets. POB adapters and other project adapters depend inward on this package and retain their domain behavior.

## Public Surface

- `ITcePresentationSource` lets a project expose custom visual capture.
- `ITcePresentationPlayer` and `TcePresentationHandle` play and dispose captured visuals.
- `TceVisualSnapshot`, `TceMeshSnapshot`, `TceSpriteSnapshot`, and `TceSpriteLayerSnapshot` are the supported snapshot models.
- `TcePresentationPlaybackSettings` controls style, tint, duration, fade window, alpha curve, material override, sprite sorting override, renderer tint property name, mesh texture property name, offset, direction, and texture copy policy.
- `SpawnSnapshotEffectData` and `SpawnSoulGhostEffectData` are catalog-visible TCE effects.
- `TceVisualPrefab` admits and reconstructs an authored visual-only hierarchy of particles, sprites, static meshes, lights, and trails. Mesh resources are shared without requiring readable CPU data.
- `TceVisualProjectionPolicy` declares source-only components, rendering data and generated-mesh adapters.
- `TceVisualDynamics` and `TceVisualFrame` capture resolved visual controls and continue display playback locally.
- `TceVisualDynamics.CreateSourceCapture(instance, authoredTemplate)` captures the live authored graph while excluding runtime-only UI/helper children. Authored node/component removal or reordering is refused. Keep the capture within that pooled activation and dispose it before the producer is reused.

## Authored visual projections

`TceVisualPrefab.TryCreate(prefab, policy, out recipe, out reason)` validates a bounded authored graph. `TceVisualProjectionPolicy` identifies source-only producers, approved pure rendering data and generated renderer adapters. Instantiation happens under an inactive parent; Animator controllers, PlayableDirectors, audio, opted-in source physics and producer scripts are removed before activation. Unknown components remain explicit failures. The host keeps source behaviour and lifetime.

`TceVisualDynamics` captures resolved animation/director clocks, light and material-property controls, trail emission and resolved 2D motion. A display uses the same authored graph and clips. Display clips are private copies with empty event arrays. A manual Playable graph targets an empty Animator with events disabled, including Sprite reference curves; no source Animator controller, StateMachineBehaviour, audio track, ITimeControl or source callback executes. Timeline supports internal Animation/Control bindings; external targets, control-prefab spawning and unknown tracks are diagnosed. Source physics is represented by resolved velocity/gravity, never a client Rigidbody. Timeline particle control exposes its resolved playback speed separately from Unity `isPlaying`, which is false during manual simulation.

Animation capture selects the dominant clip on layer zero. Timeline sampling supports visual clip curves and internal activation; it does not reproduce arbitrary Animator layer blending, track mixing or root-motion authority. Audit the consumer's actual content against this scope. Camera feedback, audio and source-script side effects require the consumer's own presentation contracts.

`TceVisualFrame` provides a bounded binary visual-control payload and validation. The host owns its network identity, source sampling cadence, transport, timebase, resource readiness and budget. `ControlsEqual` compares controls without resending ordinary continuous clock progress. `Receive` reanchors, and `Advance` continues locally with a 3-second bound. Dispose each dynamics player to release its event-free clip copies.

`recipe.Instantiate(parent)` returns an inactive display. The caller owns activation, pose, lifetime and destruction. `TceVisualVisibility` pairs whole-renderer/light suppression with exact restoration without changing authored enabled/playback states. The core has no project, physics authority, damage or network SDK dependency; renderer technology such as Spine is provided by the host's existing event-free adapter.

## Quick Integration

1. Add the Git UPM dependency `https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.tce.presentation#<tested-commit>` and its TCE dependency at the tested commit. Local paths are temporary trials.
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
