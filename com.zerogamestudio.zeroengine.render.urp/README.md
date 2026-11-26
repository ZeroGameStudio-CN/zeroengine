# ZeroEngine Render URP

Unity 6000.6 / URP 17.6 pixel-art rim lighting. `ZeroEngine.Render.Urp`
depends only on Unity and URP. Gameplay classes, actor discovery, configuration,
pooling and project assets stay in the consumer. The existing UnityEngine-only
`com.zerogamestudio.zeroengine.render` package remains independent.

## Enable

1. Add `SpriteRimRendererFeature` to each URP Renderer used by the game cameras.
2. Add `SpriteRimLighting` to an actor and call `Configure(renderers, profile)`.
   A null profile uses the reference-style color-dodge response on a one-art-cell
   contour with a faint inward transition. `ReferenceEdgeResponse` is enabled in the shared default;
   existing custom profiles retain their previous response until explicitly enabled.
3. Create a dedicated light with `GameObject > Light > 角色边缘点光` or
   `角色边缘聚光`; the Editor-only authoring assembly adds the native light and
   `SpriteRimLightSource` together, with ordinary Undo support. For an existing
   native Point/Spot, add `SpriteRimLightSource` explicitly.
   Its component checkbox enables or disables rim participation. Unmarked lights
   continue to illuminate source materials without creating an actor rim.
   URP visibility, culling masks and the camera frustum select up to four marked
   local lights per actor. Directional lights and sky illumination remain excluded.
   The reference response includes the light multiplier in RGB and coverage;
   the previous stronger-core style retains its separate hue/coverage mapping.

For authored 2D fields, create `SpriteRimLight` components and pass their explicit
array to `Configure(renderers, lights, profile)`. Both inputs use the same enhanced
composite. The old preview tint and reference MRT implementations are removed.

The default uses the already shaded scene color. Source textures supply alpha
coverage. Same-side marked native lights form one bounded field. The strongest
visible 2D center response selects the shifted art-cell mask, and Color Dodge
resolves the field once. Bright and dark art retain different responses. RGB
is bounded together so strong colored lamps keep their hue.

With opposing lights, the key keeps its directional contour and inner wrap.
The opposite field can strengthen recessed details on the far side in the
key's hue. A thin strand can also carry the key hue on its opposite-facing
contour when both fields overlap behind it. This contribution fades with the
visible fields and the rear angle measured along the camera axis. The fill
uses its own hue on uncovered broad edges and fades as key coverage grows
during a handoff. The strand contribution is softer on pale art.
Dark diagonal joints check the neighboring art cells at the edge and inside
the silhouette to retain a continuous contour.
Key selection uses unclamped
influence, a 20% challenger margin and a 0.15-second handoff. A dominant group
can steer the key, and occluded lamps drop out per art cell.

The native field follows the camera ray to each source's XY plane. Native
range, spot cone, front/back direction and shadow visibility still control
the contribution. A rear source near the projected center retains a key
side and weaker horizontal fill. Sources below the actor can illuminate
downward-facing edges.

An angled rear key carries its own hue into recessed details and one art
cell inside the far silhouette. This shares the inner-wrap strength and
the visible key field. The outer edge remains available for opposing fill.

The outer edge uses a definite alpha boundary. The shared default uses a
strength of 0.08 for the narrow inward wrap. The opposing fill gives dark
cloth a small colored reflection that fades with the source's falloff. HDR
limiting preserves existing emission and bounds added light by the available
RGB headroom.

The previous optional native core compares complete art cells along the light direction.
Near a rear light's projected center, it blends into the symmetric one-cell
backlight contour. Filtered inward wrap remains separate, so small islands,
inner leg gaps and clothing cutouts can brighten without filling the body.
The core uses the painted RGB in a bounded color-dodge response, preserving
dark cloth and pale highlights instead of adding a uniform colored line. A small
bounded visibility floor keeps black art readable; the older dark fill is reduced
while the clear core is enabled. These stronger-core controls remain available to
custom profiles with `ReferenceEdgeResponse=false`; they are not part of the new default.
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
Both response styles use the same scene-color composite.

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

The previous custom response can use `SpriteRimLightSource.DarkEdgeFill` and
`SpriteRimLightingProfile.DarkEdgeBrightness`. Reference-style edges instead retain
the material-dependent dodge response without adding a fixed-color fill.
Marked point/spot lights use the available
URP additional-shadow atlas. An optional opaque `FallbackOccluder` supplies oriented
mesh bounds when realtime shadows cannot represent that light; it does not replace
scene physics, game visibility or the original sprite material.

The shared default uses a one-cell edge, zero silhouette scaling, an LDR maximum
of one and a 0.08 two-cell inner wrap. Projects can pass an explicit profile to `SpriteRimLighting.Configure` for
their own appearance. The body/weather backends and their package dependency remain
available independently of this rim-light extension.

Runtime body flames require the built-in `com.unity.modules.particlesystem` module,
now declared by the core Render package. Synthetic rendering tests create a temporary
URP pipeline if the host project has none and restore the previous quality override.

Native point and spot rim participation retains a symmetric rear contour at the projected actor center and stays continuous as the light crosses the center. Front-centered lights do not become rear silhouette lights. This preserves the POB fix from `8b06c811` on the consolidated package baseline.
Version 0.4.3 requires Render 0.4.4 at the same Git commit, including the existing blood-resource loader and shaders.
It also preserves `faafbd1f`'s thin rear and opposed-light contours, including the faint inward transition and continuous center projection.
The adopted `e54a5509` correction excludes occluded native lights from each art cell's directional style mix, preserving the visible light's contour and transition.
