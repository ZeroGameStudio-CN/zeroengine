# ZeroEngine Render URP

Unity 6000.6 / URP 17.6 pixel-art rim lighting. `ZeroEngine.Render.Urp`
depends only on Unity and URP. Gameplay classes, actor discovery, configuration,
pooling and project assets stay in the consumer. The existing UnityEngine-only
`com.zerogamestudio.zeroengine.render` package remains independent.

## Enable

1. Add `SpriteRimRendererFeature` to each URP Renderer used by the game cameras.
2. Add `SpriteRimLighting` to an actor and call `Configure(renderers, profile)`.
   A null profile keeps a clear contour with a faint inward transition over two
   art cells. The deeper artwork retains its original exposure.
   ContourStrength adds a clear one-art-cell core to marked native lights,
   including small isolated cells and the edges of gaps inside a silhouette.
   The shared default uses 1; older custom profiles keep zero until enabled.
   The existing soft band and inward transition retain their own settings.
3. Create a dedicated light with `GameObject > Light > 角色边缘点光` or
   `角色边缘聚光`; the Editor-only authoring assembly adds the native light and
   `SpriteRimLightSource` together, with ordinary Undo support. For an existing
   native Point/Spot, add `SpriteRimLightSource` explicitly.
   Its component checkbox enables or disables rim participation. Unmarked lights
   continue to illuminate source materials without creating an actor rim.
   URP visibility, culling masks and the camera frustum select up to four marked
   local lights per actor. Directional lights and sky illumination remain excluded.
   HDR intensity controls coverage separately from hue, preserving colored edges.

For authored 2D fields, create `SpriteRimLight` components and pass their explicit
array to `Configure(renderers, lights, profile)`. Both inputs use the same enhanced
composite. The old preview tint and reference MRT implementations are removed.

The clear native core compares complete art cells along the light direction.
Near a rear light's projected center, it blends into the symmetric one-cell
backlight contour. Filtered inward wrap remains separate, so small islands,
inner leg gaps and clothing cutouts can brighten without filling the body.
The core uses the painted RGB in a bounded color-dodge response, preserving
dark cloth and pale highlights instead of adding a uniform colored line. A small
bounded visibility floor keeps black art readable; the older dark fill is reduced
while the clear core is enabled. Zero ContourStrength keeps the old response.
Core strength controls the blend rather than reducing the input light energy.
The one-cell core applies native distance and shadow visibility once. The softer
shell keeps its separate response, so brighter edges do not raise body exposure.
It adds no new HDR emission and preserves existing HDR source energy. Multiple colors
blend at the strongest core contribution's peak. A gentler core-only color weight
keeps weaker colored lamps visible through the handoff without raising the inward
field. Identical lamps do not multiply brightness, and light order does not change
the result. Shadow visibility applies to the core and to the existing light response.

Consumers can enroll their Player/entity renderers at their normal enable and
renderer-replacement entry points. Disable the lighting component when the actor
is disabled; reconfigure when its renderer changes. Re-enabling rebuilds its owned
draws, making pooled reuse independent of the previous actor. No scene-wide actor
scan or per-frame light scan is needed.

## Rendering and ownership

The feature runs after opaque/alpha-clipped drawing. Depth prevents recoloring
occluded or dissolved pixels; transparent source materials retain their own
rendering and are skipped. Transparent overlays render normally afterward.
The system does not replace shared materials or modify source property blocks.
Its own materials/masks are released on disable or target replacement.

The mask follows Sprite pixels-per-unit, pivot, flips and world transforms.
For enlarged textures or custom/unreadable meshes, configure `SpriteRimPixelGrid`.
Art cells retain point-sampled source color; only contour coverage is filtered.
Incoming HDR/emission is preserved, and the profile controls added highlight
limits and a weak inward light wrap. This is stylized depth-aware recoloring;
it does not add normal maps or independently reproduce shadow attenuation.

Only registered actors visible to a game base camera update their silhouette.
Scene lights are captured from that camera's URP culling results. Disabling an
actor or removing its light removes its contribution in the same camera render.
Existing profiles keep this ZE path. The explicit original-mechanism mode below
is independent; it is never selected as an automatic fallback.

## Original mechanism compatibility

Set `SpriteRimLightingProfile.UseOriginalMechanism` for the recovered
light/rim/exposure model. Enrollment still uses `SpriteRimLighting.Configure`;
there is no second actor component or source-material replacement. Use explicit
`SpriteRimLight` inputs for the original 2D Point, Directional and Spot fields.
The reconstruction uses the shipped Lost Ruins 1.0.9a sprite/light programs,
camera/render-target configuration and PlayerRim preset as its reference.

This path clears a non-sRGB 480×270 RGBA8 field to black with alpha **one**,
then accumulates every field light with `SrcAlpha/One`. Its light blend therefore
does not inherit the extra weak-light attenuation of a transparent-black field.
It chooses one strongest rim-enabled light at the receiver's
transform pivot. The selector's intensity does not depend on light color.
`AffectsRim=false` retains that light's field contribution without letting it
choose the mask direction. The recovered Spot selector intentionally differs
from the Spot field shader: it uses angle/range while the field uses
1−angle/range. Equal strengths retain the first source's direction.
`SpriteRimLighting.OriginalIgnoredLights` provides per-receiver exclusions for
direction selection without removing those colors from the field. The recovered
manager's maximum selection distance defaults to 4 in normalized light space.

The source Sprite alpha, pivot and rectangle drive the shifted mask directly:
there is no art-cell snapping or alpha=0.5 threshold. The original
`TransformVector` offset and scale response are retained. The PlayerRim defaults
are distance 0.04→0.015 (100 texture-texel units), scale 0→−0.05, alpha 0→1,
response powers 0.5, local exposure minimum 0.9, blend 1 and maximum light 3.
`SpriteRimOriginalSettings` also exposes all nine original blend modes and
thresholded exposure. Color Dodge retains the shipped saturated-channel branch.

Unlike the ZE composite, this mode uses texture color before scene lighting and
reconstructs the original Gamma-space blend in a Linear URP project. Dark blue
and other nonzero dark colors therefore retain their material-dependent
response, without a uniform brightness floor. It also retains the original
additive saturation and highlight behavior. The ZE highlight cap, inward wrap
and independent per-light contours do not participate in this mode.

This is a reconstruction of the active light/rim/exposure mechanism, not the
original game executable or every optional source-shader feature. The URP
adapter uses the existing opaque/alpha-clipped depth coverage; transparent
source materials are skipped. Posed meshes use the current posed silhouette
adapter because the original source-alpha shift targets SpriteRenderer art.
Native marked Point/Spot lights can supply mapped 2D inputs; this mapping is
separate from the original model and does not reproduce URP 3D attenuation or
its light-shadow atlas. Keep the ZE model for that shadow-aware workflow.

Verification includes synthetic replay against the shipped compiled sprite and
three light-shape shaders, plus portable golden samples in
`SpriteRimOriginalGoldenSamples`. The package ships only its own implementation
and synthetic inputs/results; no game binaries or artwork are included.

## Validation and consumption

Run `ZeroEngine.Render.Urp.Tests.Editor` for light selection and assembly ownership,
then `ZeroEngine.Render.Urp.Tests.Runtime` for real Render Graph output, native
local lights/culling and daylight exclusion, opposed lights, light wrap, pivots, flips, scaling, occlusion,
transparent sources and restoration. Consumer actor lifecycle and real entry
remain consumer tests. No packaged Player or device performance claim is implied.

A consumer's startup or quit save hooks can run even in package-only rendering
tests. Run these PlayMode cases through that consumer's whole-Play storage scope;
keep consumer-specific save adapters outside this package.

Use the official Git UPM URL pinned to the tested commit. A local `file:` URL is
for temporary verification only. Add this package to the consumer's `testables`
list when running the package tests.

## Body visual backend

The optional body visual resources are independent of rim-light registration.
`BodyVisualResourcesLoader.Load()` returns namespaced body/surface/speck shaders.
Authored sample/final variants keep their material property names and mask pass
indices. The backend depends on `ZeroEngine.Render` 0.3.0 and URP 17.6; there is
no POB, Buff, Addressables or third-party shader dependency.

Consumers may retain compatibility shader assets using the named `UsePass`
entries so existing material GUID references remain valid. Blood palette math
uses the render package's neutral `VisualPalette.hlsl` with caller-provided
material values, never a game-specific include or setting. Original palette
mode is the default. Install this package and the render package from the same
tested commit. Local file paths are verification-only pins.

The package body tests cover caller-clocked playback, current-pose masks, fixed
pixel emission, source composition and borrowed asset cleanup. A consumer must
also check real game-camera output before adopting an extraction.

## Surface-footstep material wrapper

`Runtime/Shaders/SurfaceFootstep.shader` preserves POB's alpha blend and packed UV stream
contract: UV.xy, normalized age.z, stable random.w and vertex color. It delegates all eight
shape masks to ZeroEngine Render's portable include. Callers own a pooled emitter,
contact positions, motion, material instances/property blocks, audio and expiration.
The Godot counterpart is a native shader wrapper; Unity Prefabs/ParticleSystem modules
are not embedded or automatically translated by this package.

## Weather-aware ground wrapper

`Runtime/Shaders/WeatherSurface.shader` reads independent wetness and snow values
from its consumer. Wetness darkens the base and raises smoothness; snow blends an
upward-facing grain mask. The material keeps URP PBR lighting, main-light shadows
and fog. Up to eight caller-provided world-space roof bounds suppress exposure.
It has no WeatherManager, actor, cell, save, audio or pool dependency.

A consumer with an existing shader should integrate the neutral
`ZeroApplySurfaceWeather` layer and keep its own lighting/UV model.
The standalone wrapper supports opaque flat ground using base map/color, metallic
and smoothness. The consumer must preserve those properties and release its own
material instances. This is not an automatic conversion of arbitrary vendor
shaders, transparent water, normal maps, terrain layers or baked lightmaps.

Footstep style IDs are stable: 0 ripple, 1 splash, 2 dust, 3 spark, 4 leaf,
5 snow, 6 petal and 7 chip. Particle contact/motion, packed age/random streams and
expiration stay with the caller; a teleport resets the sampling/emitter origin
and retires live particles without resetting regional ground state. The generic
Render package contains no product audio, textures or extracted game assets.

## Automatic scene surface weather

`AutomaticSurfaceWeather` owns one project-level `SurfaceWeatherSceneProfileSO`.
Initialize the profile with `SceneWeatherLit`, `SceneWeatherTerrain` and
`Hidden/ZeroEngine/SurfaceWeatherExposure`, then feed `Field.DefaultValue` and
`Field.SetRegions` from the existing world-weather owner. Disabling/destroying
the component releases its scope. Ordinary placed, copied, moved and pooled
objects require no weather component, receiver list or per-instance material edit.

The adapter discovers MeshRenderers and Terrain from resident scenes and the
persistent scene. Initial/scene-load discovery and bounded periodic hierarchy
visits register new instances. Materials are borrowed once per shared source;
textures, UVs, normal maps, keywords, transparency and original material references
remain with their owners. Native shader families opt in through the hidden
`_ZeroSceneWeatherReceiver` property and `SceneSurfaceWeather.hlsl`; native hooks
must gate their weather branch on that per-material value. A consumer with a
separate appearance overlay can use `SurfaceWeatherMaterialSources.Resolve`.

The supplied Lit and Terrain wrappers adapt the URP 17.6.0 forward/deferred
surface fragments while retaining upstream vertex, lighting, splat, hole, depth
and shadow paths. Upstream source attribution/license accompanies these adapters
in `Runtime/Shaders/SceneWeather/UPSTREAM-LICENSE.md`. Other custom/vegetation
shader families require one family adapter; unsupported families retain their
original materials and are listed by `UnsupportedShaders`. Terrain trees/details
using their own shaders are not claimed as supported by the Terrain surface wrapper.

Exposure is rasterized from the real mesh geometry, including non-readable meshes,
and a bounded Terrain height proxy. Roof and indoor parts of a combined mesh are
sampled separately. Visually disabled cutaway renderers remain physical geometry;
inactive pooled objects and shadow-only actor proxies do not act as roofs. Texture
cutouts are respected. Transparent Lit glass can still block precipitation.
The profile can classify known liquid/effect shaders as non-receivers and generated
nonphysical backdrop materials as non-occluding, once for the project.

Two fixed-size, double-buffered exposure maps support the rain direction and
vertical snow. Each async result is copied in its completion callback, since a
readback's data expires after its serviced frame; both are published together.
There is no per-frame scene-wide search, unbounded material history, synchronous
per-frame readback or independent weather evolution. Discovery, object counts,
map resolution (64–1024) and rebuild interval are bounded by the profile. Exposure
precision depends on the resident geometry extent and configured resolution.
GPU capability/readback or capacity failure is exposed by `LastFailure` and does
not masquerade as complete coverage. Moving geometry has a bounded refresh latency.

The permanent contract fixtures are `SurfaceWeatherFieldTests` and
`AutomaticSurfaceWeatherTests`. Enable the two packages in a validation project's
testables, and run only these fixtures for this feature. A product must also inspect
actual game-camera output and verify its regional weather, cutaway, contact and
session/streaming adapters before adopting the candidate.

### Material recipe changes

Registered MeshRenderers react to `SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(renderer)` synchronously. Only that tracked renderer is rebound through the existing material cache and released recipe; new objects keep the bounded discovery path. Registration budgets and authored shadow-proxy materials remain unchanged. Dispose removes the notification subscription.

## Marked native lights and dark contours

`SpriteRimLightSource.DarkEdgeFill` adds bounded light color to dark contour cells;
zero retains the original fill response. `SpriteRimLightingProfile.DarkEdgeBrightness`
limits added brightness on dark artwork. Marked point/spot lights use the available
URP additional-shadow atlas. An optional opaque `FallbackOccluder` supplies oriented
mesh bounds when realtime shadows cannot represent that light; it does not replace
scene physics, game visibility or the original sprite material.

The consolidated default asset retains the adopted POB inner-wrap and dark-edge
values. Projects can pass an explicit profile to `SpriteRimLighting.Configure` for
their own appearance. The body/weather backends and their package dependency remain
available independently of this rim-light extension.

Runtime body flames require the built-in `com.unity.modules.particlesystem` module,
now declared by the core Render package. Synthetic rendering tests create a temporary
URP pipeline if the host project has none and restore the previous quality override.

Native point and spot rim participation retains a symmetric rear contour at the projected actor center and stays continuous as the light crosses the center. Front-centered lights do not become rear silhouette lights. This preserves the POB fix from `8b06c811` on the consolidated package baseline.
Version 0.4.3 requires Render 0.4.4 at the same Git commit, including the existing blood-resource loader and shaders.
It also preserves `faafbd1f`'s thin rear and opposed-light contours, including the faint inward transition and continuous center projection.
The adopted `e54a5509` correction excludes occluded native lights from each art cell's directional style mix, preserving the visible light's contour and transition.
