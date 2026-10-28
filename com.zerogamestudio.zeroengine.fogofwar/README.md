# ZeroEngine Fog Of War

Caller-driven fog for XY worlds, independent of a game's actors, inventory, saves,
UI, object pool, clock and render pipeline. Unity 6000.6 is the initial target.

## Assemblies and ownership

- `ZeroEngine.FogOfWar`: engine-free explicit target registration, generation-stamped
  handles and synchronous current-position queries. No scene scanning or frame clock.
- `ZeroEngine.FogOfWar.Unity`: geometric line of sight, exploration memory,
  presentation-only Renderer/Canvas targets and a policy-driven compatibility scanner.
- Optional `com.zerogamestudio.zeroengine.fogofwar.urp`: camera-sized GPU masks and
  soft edges. Logical visibility stays on the CPU and never waits for GPU readback.

The game supplies solid segments/terrain meshes and owns collider selection, actor
identity, player exemptions, interaction policy, save envelopes and scheduling.
Hiding affects presentation only. It never disables actors, AI, collision or timers.

## Install

Pin a tested upstream commit through UPM; replace `<tested-commit>` with that hash:

```json
"com.zerogamestudio.zeroengine.fogofwar": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.fogofwar#<tested-commit>"
```

All ZeroEngine packages in a consumer should use its tested compatible commit set.
Add this package to the consumer manifest's `testables` only while running its tests.
Never ship a development machine's absolute `file:` dependency.

## Registered targets

```csharp
using ZeroEngine.FogOfWar;
using ZeroEngine.FogOfWar.Unity;

var sight = new SightPolygon2D();
var world = new VisibilityWorld();
var query = new VisibilityQuery2DBridge(sight);
var target = new PresentationVisibilityTarget2D(actorTransform, actorRenderers, actorCanvases);
var handle = world.Register(target);

// Caller publishes the current geometry and eye position, then updates presentation.
sight.Rebuild(eye, radius, facing, forwardExtension, solidSegments);
world.Evaluate(query);

// Pool return/despawn: release this registration's owned presentation state immediately.
world.Unregister(handle);
world.Dispose();
```

Registration is idempotent by target reference. Handles cannot remove another
world's target or a replacement in a reused slot. Each evaluation reads positions
again, including repeated calls within one frame. Callback removals take effect
immediately; callback additions are visited in the same call. Nested evaluation is
rejected, and adversarial callback registration churn has a bounded budget.
Exceptions propagate; the caller decides whether to stop/retry. `Dispose` releases
all targets even if a target's release throws. This is a single-threaded API.

Supply a complete graphic roster for each target and give each graphic one owner.
Moving/reparenting an anchor needs no notification. Replacing a graphic roster
requires replacing its registration. Authored disabled/externally suppressed state
is preserved; a target releases only the overrides it acquired. Do not combine the
registered path and `SceneVisibility2D` on the same graphics.

## Compatibility discovery

`SceneVisibility2D` retains synchronous scene discovery when a game cannot yet
provide complete registration/dirty notifications. Implement `VisibilityScenePolicy2D`
to identify primary/secondary owners, content components/layers, and optional canvas
interaction exceptions. Owner creation revisions must advance on construction,
including inactive objects. Return fresh positions and exemptions in policy queries.

It checks current parents/layers and invalidates apply-local answers after presentation
callbacks. The bounded ancestor path is optional per world and falls back for dense
roots. This path intentionally still pays for scene discovery; moving it into a package
does not make that cost disappear. Use registration where the lifecycle contract can
be guaranteed, and keep discovery for unregistered/dynamic legacy content.

In 0.2, `SceneVisibility2D.RegisteredVisibilityEnabled` opts into an incremental
graphic roster on Unity 6000.6. It bootstraps Renderer/Canvas membership once,
then consumes synchronous engine creation, destruction, component, layer and
hierarchy notifications. An ancestor dependency index refreshes only affected
ownership bindings; ordinary motion still reads current positions and sight.
Structural Transform and RectTransform type notifications cover sibling edits and
ancestor reparenting without per-Apply ancestor scans or TransformChangeDispatch
listeners. The warmed reader reuses its managed object list and disposes native
temporary arrays; no managed change array is returned.
Presentation callbacks drain changes before later graphics are evaluated. Inactive
pooled graphics remain registered, and disposal releases only owned overrides.

This version-qualified bridge uses internal `UnityEngine.ObjectDispatcher`
through an isolated reflection binding, with a linker preservation entry. Check
`RegisteredVisibilityActive` and `RegistrationUnavailableReason` in the actual
Player. Unsupported/stripped bindings, engine assembly changes or roster capacity
overflow retain synchronous compatibility discovery. The package default stays
off; each consumer must qualify its engine/platform before enabling it.
`RegisteredBootstrapQueries`, `RegisteredBindingResolutions` and
`RegisteredGraphicCount` expose roster maintenance without doing a diagnostic scan.

## Exploration and persistence

`ExplorationGrid2D` stores explored cells and last-observed terrain separately from
precise current sight. `SetCurrentSight` supplies sub-cell wall accuracy; `Reveal`
updates memory. `ExportRevealed` and `ExportRememberedTerrain` return neutral base64
payloads. The game owns IDs, schema versions, storage and migration. Validate those
before `TryRestoreMemory`; invalid payloads do not mutate state, and restoring memory
never restores current sight. Geometry updates do not reveal unseen changes.

`Upload` publishes RGBA8: explored / remembered solid / remembered edge / current sight.
Rendering can soften an edge without weakening authoritative sight tests.

## Verification

`ZeroEngine.FogOfWar.Tests.Editor` covers synchronous membership/callbacks, stale
handles, pool cleanup, allocation-free warmed core evaluation, sub-cell walls,
atomic memory restore, authored presentation state and assembly dependencies.

0.2.1 retains an ancestor index for inactive pool entries. Activation and reparent
notifications update cached participation; warmed snapshots skip inactive and known
non-content graphics without per-entry native active-state queries. Current sight
and positions still refresh on every Apply.
