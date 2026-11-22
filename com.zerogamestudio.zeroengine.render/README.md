# ZeroEngine Render

Small UnityEngine-only rendering primitives. No TCE, gameplay, pooling service,
project asset or third-party plugin dependency.

## Automatic scene surface weather contracts

`Weather/SurfaceWeatherField` is a read-only world-space projection of a consumer's
existing wetness/snow state. It has no weather clock, camera target or save format.
Up to 32 stable XZ regions blend with normalized weights; equal-priority neighbours
do not introduce a dry strip. Higher priorities override lower priorities. Invalid
replacement leaves the previous field intact.

`SurfaceWeatherProjection` and `SurfaceWeatherExposureFrame` share the same bounded
projection, surface bias and four-tap exposure sampling as `SceneSurfaceWeather.hlsl`.
The URP adapter publishes its CPU pixels and GPU textures as one generation. Callers
sample actual contact positions, including any product-owned visual-ground offset.
`SurfaceWeatherExposure.Current` exposes the one scoped geometry publisher to
contacts and precipitation without giving those readers a rendering dependency.

The consumer retains world state and artistic choices. `SurfaceWeatherMaterialSources`
lets another presentation layer identify an original material behind a temporary
weather borrow; the URP owner unregisters that mapping when releasing the borrow.
There is no dependency on a product scene, actor, material library or content name.

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

## Portable footstep fragment math

`Runtime/Shaders/SurfaceFootstepMath.glslinc` implements `zero.render.surface_footstep.v1`,
adapted from POB's owned WaterFootstep implementation. It provides ripple, splash, dust,
spark, leaf, snow, petal and chip masks from caller UV, normalized age, stable random
and profile uniforms. It does not read engine time or scene textures. Engine-specific
wrappers own shader aliases, blend state, vertex streams, clocks, particle motion and
budgets; no engine nodes or particle modules are automatically translated. This is an extraction of our implementation,
not Octopath game code/assets.

## Optional wetness / snow layer

`Runtime/Shaders/SurfaceWeatherMath.hlsl` provides `ZeroApplySurfaceWeather`.
It accepts base color, smoothness, world position/normal, independent wetness/snow
and at most eight roof bounds, returning color plus smoothness. Zero state is
identity. The caller retains UV mapping, pixel style, lighting, cutaway, alpha
and resource lifetime. This layer does not replace a consumer's authored shader.
Upward-facing snow blends continuously; vertical faces and sheltered ground do
not receive coverage. The URP package also offers a plain opaque PBR wrapper.

`SurfaceWeatherField` and `SceneSurfaceWeather.hlsl` use the same region weights.
Overlapping regions at the highest participating priority are normalized together.
When their total coverage is below one, the remaining coverage uses `DefaultValue`;
an isolated region therefore fades into the background through its outer feather.
Zero feather retains an explicit hard edge. The field owns no weather clock or save data.

### Presentation-owner material changes

After replacing a renderer material recipe, call `SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(renderer)`. Active adapters can immediately rebind renderers they already track. This notification does not discover new objects or mutate materials by itself. Resolve borrowed weather materials before retaining an authored source; never retain a temporary weather instance as another owner's restoration source.

## Caller-clocked surface attachments

ZeroEngine.Render.Attachments provides the shared XY-plane attachment algorithms:

- SurfaceAttachmentPose keeps a supplied local tip pinned to a moving or mirrored reference and evaluates the elastic landing from caller time. Apply returns true once settled; the host owns stopping, reset and lifetime.
- SurfaceAttachmentPose.SampleCounterRecoil returns a normalized first swing and a shorter, smaller opposite swing. Pass elapsed phase after seating, with 1 at the first return through the incoming angle, then scale the result by the peak angle. The host chooses the phase rate, peak angle and when to use the response.
- SurfaceAttachmentPlacement.Resolve finds a bounded geometric contact inside the authoritative hit region. TryFindVisibleContact separately follows the current opaque renderer surface, adapting probes to local scale/rotation while leaving the hit unchanged. Reach and minimum exposed span come from the moving visual, rather than a monster profile.
- EmbeddedSpriteAttachment borrows a sprite and target renderer, crops the configured hidden tip span, aligns the visible endpoint across camera depth and restores sprite, local position and prior rendering visibility on release. Call UpdateDepth before applying the attachment pose, then SetContactVisibility after the target's current animation/mesh update. Restore ends the visual lease.

The host supplies hiddenTipLength in sprite-local units, a finite depth offset, the local tip, contact, target region and clock. POB keeps its existing art values and serialized component types; its TCE recall Effect continues through the native projectile owner. Runtime types have no TCE, Core, damage, recall, pooling, save, Spine SDK, or project-asset dependency. Other TCE effects or state machines call these primitives through their own lifetime adapter; extraction needs no duplicate graph Effect.

Supported coverage sources are Simple SpriteRenderers and readable triangle MeshRenderers with one atlas (_MainTex or _BaseMap). UV coverage is cached within 16 MiB total and 4 MiB per atlas, using readable pixels or one asynchronous GPU readback. Unknown/unavailable coverage reports no proven visual contact. Mesh limits are 8192 vertices / 24576 indices. Cropping supports unpacked sprites; packed assets retain the borrowed sprite. Materials and source textures remain borrowed. Generated crop sprites are session caches reset on subsystem registration.

The APIs run synchronously on the Unity main thread and are not thread-safe. They preserve the current XY contract rather than promising arbitrary 3D skin attachment. Target lifetime, extraction damage, restore-before-pool behavior and gameplay registration remain host responsibilities.

Focused verification: ZeroEngine.Render.Tests.SurfaceAttachmentTests, then consumer projectile recall/embedding checks and real gameplay capture. Package-only tests do not establish player release or human visual acceptance.

For projectile flight-to-contact transitions, `SurfaceImpactMotion` takes the incoming direction, speed, spin and a stable visual sequence. It carries entry motion while applying angular recoil with the tip fixed, and supports different clearance and rebound weights for geometry and moving bodies. Keep contact detection, surface selection and gameplay stop decisions in the host.

For a resolved hit that already has an authored contact anchor, add `RetainedProjectileSprite` to the presentation sprite. Call `Begin` with the contact, incoming orientation and a target-lifetime predicate. It follows the anchored point in 2D or 3D and applies the shared counter-recoil before fading. Pass a pool-return callback; each visual expires independently. Call `End` on invalidation or teardown. The package owns visual playback and lease cleanup. The game passes the contact only after its resolved projectile comes to a stop and chooses which hits should keep a mark.
