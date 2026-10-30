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
No optional legacy rendering path or automatic fallback is installed.

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
