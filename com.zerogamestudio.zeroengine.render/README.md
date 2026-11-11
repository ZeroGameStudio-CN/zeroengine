# ZeroEngine Render

Small UnityEngine-only rendering primitives. No TCE, gameplay, pooling service,
singleton, project asset or third-party plugin dependency.

## Body visual bounds reuse (0.3.1)

`BodyVisualHost.SetBodyBoundsCacheEligibility(predicate)` opts eligible ordinary
SpriteRenderers into exact native world-centre reuse within each existing Render
pass. The default keeps the original native reads. Callers must reject custom
world-bounds overrides, skinning/deformable buffers and unknown pose providers.
`BodyVisualBoundsCache` rechecks renderer identity, exact transforms/local bounds,
sprite geometry bounds, flips, draw mode and size at every use, and resamples immediately
when they change. It never skips pose capture, clocks, composition or callbacks,
and begins fresh for each Render call, including synchronous Play/Remove calls.
Legacy `BodyVisual.Update` and constructor signatures remain available.
The diagnostics method `GetBodyBoundsReadCount()` reports the latest pass.

Run `BodyVisualBoundsCacheTests` plus the consumer's ordinary, skinned, custom-pose
and pooled-lifetime cases. Bind performance claims to the measured caller/workload.

## RendererOpacityBinding

Create one binding for each caller-owned effect renderer. `Apply(multiplier)`
restores the previous override before reading current authored inputs, preserving
sprite/material animation, shared materials and unrelated property-block values.
Dedicated opacity properties take priority over color alpha. Particle, sprite,
trail, line and mesh renderers share this path; zero suppresses rendering without
disabling gameplay objects. `Restore`/`Dispose` returns only still-owned changes.
The caller owns discovery, source classification, frame ordering and pool hooks.
Untouched full-intensity bindings and hidden applications skip material discovery.
Channels are created lazily on the first partial fade. Previously injected neutral
values still follow material animation when full intensity is restored.
Partial fades validate ownership and apply the new tint in one property-block
read, while retaining changes made by another writer between frames.
Call `PrepareFrame` before animation scripts to restore color/fade inputs while
keeping a zero-opacity visibility override. Policy changes and pool return use
`Apply`/`Restore` to release that override. `NeedsMaterialFollow` exposes only
previously injected values that still follow material animation. When the material
input is unchanged, following a neutral value avoids reading its property block.

Shaders without a usable opacity input, including cutout-only layers, can retain
their appearance at intermediate values. The optional unsupported-material callback
reports that boundary; the binding does not rewrite shaders or change render state.
Use `RendererOpacityBindingTests` and the consumer's actual camera for verification.

## SpritePoseHistory

Construct with distinct, caller-owned `SpriteRenderer` targets. `TryCapture`
freezes a non-null sprite's frame, world TRS, flips, color and sorting into one
fixed-capacity record. Only Simple draw-mode sprites are supported; sliced/tiled
renderers are rejected. Duration and tint are per-capture inputs. A full buffer
returns false unless `replaceOldest` is explicitly true. Invalid capture leaves
the old playback unchanged; passing a target as source is rejected.

Call `Advance(delta, intensity)` with the owner's clock. Zero, negative or nonfinite
delta does not advance age. Intensity is clamped and changes visibility only.
Expiry and `Clear` disable targets and release borrowed sprite references, without
destroying renderers, textures or materials. Source reuse/destruction cannot alter
an existing record. Capture/playback after construction has no managed allocation.
TRS does not promise exact shear reconstruction. Material choice stays with targets.

POB keeps its serialized sampling component and delegates just snapshot playback.
Existing TCE Presentation owns one-shot mesh/layered/ghost graph effects; this
high-frequency fixed-capacity path does not replace that runner or duplicate a TCE
effect system.

## FlowPattern.hlsl

Include `Runtime/Shaders/FlowPattern.hlsl`. Scalar functions use the same source and
GPU vectors as Godot `zero_render/flow_pattern.gdshaderinc`. Distances/periods share
the caller's spatial unit and phase is in cycles. Closed periods fit an integer
number of repetitions; wrapped dash distance treats both ends and the loop seam
equally. Tracer, open-line dash and chevron functions preserve POB's current math.
Inputs must be finite. Geometry, AA footprint, style, material and time are supplied
by the caller; the include never reads engine time or scene resources.

## Verification

Run the `ZeroEngine.Render.Tests.Editor` assembly, plus affected consumer tests.
It checks bounded lifetime/overflow, snapshot independence, cleanup, invalid input,
managed allocations and actual GPU results against `render_flow_vectors.json`.
Those vectors must match the Godot fixture byte-for-byte. Actual gameplay camera
checks are additionally required for visible consumer changes. Package tests do
not prove platform/export or human visual acceptance.

Consume through the official ZeroEngine Git UPM URL pinned to a tested commit.
Local file references are temporary verification only, never shared-project pins.

2026-09-12 Windows verification (Unity 2022.3.62f3, URP 14.0.12): package EditMode
8/8 including 20 GPU vectors and allocation checks; POB blade/telegraph regression
85/85; isolated real-entry PlayMode 1/1 (Intro, base, three warning shapes, stop and
cleanup). Actual game-camera captures were inspected. The test kept 94 monitored
configuration/material/settings files unchanged; its temporary fixture was removed.
Godot counterparts passed their own 468 tests and 20 GPU vectors, and the independent
game's pinned adoption passed 292 tests after its separate naming/asset cleanup.
These results do not qualify other render pipelines, platforms or exported Players.

## Caller-clocked body visuals

`BodyVisual` renders a borrowed Sprite/Mesh/SkinnedMesh pose supplied through
`IRenderPoseSource`. `RenderPoseSnapshot` owns copied meshes, while sprites,
textures and authored materials remain borrowed. `RenderPoseRenderer` owns only
its overlay views. The current body plane is XY; this API does not promise an
arbitrary 3D skin-surface effect.

`BodyVisualHost` merges appearances by kind while retaining independent source
leases and clock functions. Call `Advance` after the pose producer; dispose a
lease when its logical source ends. Timed views, retirement tails, shared small
particle budgets and resource-retain callbacks use the supplied clocks. The
host has no Buff/Core/Addressables/Spine SDK or wall-time dependency. A frozen
actor may supply an independent gameplay-domain clock for its ice appearance.

`BodyVisualSettings` and `VisualPalette` describe appearance only; they never
apply damage, freeze controls, stats or save settings. Pipeline shaders are
supplied by `BodyVisualResources`. The URP package provides ready-made resources.
Authoring presets and game-specific lifetime/loading adapters remain consumers.

## Pixel blood (0.3.6)

Opt-in, caller-clocked cosmetic blood for any explicitly supplied actor. `BloodBurstView`
adds stepped volumes, thin strands, swept XY terrain contact, flat stains and bounded
flesh/bone residue to a borrowed native particle root. `BloodImpactView` supplies smaller
hit jets or a death supplement; it preserves native cinematic death emitters.

`BloodSurface` overlays the current Sprite/Mesh/SkinnedMesh layers without replacing
source materials. Its 64x64 source-colour lookup uses a fixed seed and symmetric source
features so dye follows the source animation and flips. This is colour/feature-attached
dye, not body-part skinning; identical source features intentionally share treatment.
New splashes accumulate, hold for 3 caller seconds and fade over 5. No position or time
noise selects the attached pattern. High accumulated load fills and deepens the same
attached pattern while preserving translucent hair and dark outlines; small splashes
retain their scattered coverage. Pair with URP 0.2.3 at the same tested Git commit.
Skinned meshes require per-update BakeMesh;
large skinned crowds need their own target-device measurements.

Create a `BloodVisualContext` with explicitly supplied shaders/resources (the URP package
has a loader), camera, terrain mask, one-way layer, clock and optional colour mapper.
Pass an `IDisposable` particle-pool lease to Play; the view releases it on Stop. Keep the
borrowed particle/template dependencies resident until all consumers have stopped.
The optional `PrepareAuthoredSpray` callback can apply the consumer's palette component
to an isolated cosmetic clone; the source template is never changed.
New native spray clones stay inactive until LateUpdate safely removes copied components,
then become visible before rendering. Animation/physics hit callbacks never destroy
components immediately. Manual render drivers call `PrepareForRender` from their safe
update phase; normal MonoBehaviour use needs no extra call.

For surfaces, Bind, Add, enqueue in `BloodSurfaceUploadQueue`, then Advance, Pump once
per rendered frame, and Render your explicit renderer list. The queue coalesces each
surface, limits uploads to 8/frame by default and rejects old pool-generation tickets.
Call ResetForPool on disable/death and Dispose when retiring the actor view. Stop all
burst/impact views and reset/dispose surfaces before disposing their shared context.
ClearScope invalidates detached tails and clears only that context's residue/queue.
Set `RetainResidue=false` when no persistent visual scope is available; settled debris
then keeps the emitter's own safety tail instead of entering the residue store.

The package owns visual resources and bounded residue (60 retained + 60 retiring),
not damage, kill credit, actor discovery, room events, pooling services, saves or quality
policy. Consumers decide active/cold-spawn limits and fallback. No colliders or rigid
bodies are added. This is presentation only, with no POB or third-party game dependency.
