# ZeroEngine Render URP

Unity 6000.6 / URP 17.6 pixel-art rim lighting. `ZeroEngine.Render.Urp`
depends only on Unity and URP. Gameplay classes, actor discovery, configuration,
pooling and project assets stay in the consumer. The existing UnityEngine-only
`com.zerogamestudio.zeroengine.render` package remains independent.

Version 0.2.1 declares Render 0.3.1 and is intended to be pinned with Render to
the same tested Git commit. Body bounds reuse is an explicit consumer-side opt-in;
this dependency update does not change the URP passes, shaders or rim-light settings.

## Enable

1. Add `SpriteRimRendererFeature` to each URP Renderer used by the game cameras.
2. Add `SpriteRimLighting` to an actor and call `Configure(renderers, profile)`.
   A null profile keeps a clear contour and a two-art-cell inward transition.
   InnerWrapStrength controls that transition. DarkEdgeBrightness sets the
   added-light cap on dark art, independently of the original bright pixels.
   The shared default uses 0.22 inward strength and a 0.42 linear dark-art cap;
   existing custom profiles keep their serialized settings and a 0.25 cap.
3. Create a dedicated light with `GameObject > Light > 角色边缘点光` or
   `角色边缘聚光`; the Editor-only authoring assembly adds the native light and
   `SpriteRimLightSource` together, with ordinary Undo support. For an existing
   native Point/Spot, add `SpriteRimLightSource` explicitly.
   Its component checkbox enables or disables rim participation. Unmarked lights
   continue to illuminate source materials without creating an actor rim.
   URP visibility, culling masks and the camera frustum select up to four marked
   local lights per actor. Directional lights and sky illumination remain excluded.
   Assign an opaque door or wall MeshRenderer as the optional fallback occluder
   on a shadow-casting marked light. When the camera has no packed URP shadow
   index for the light, its oriented bounds block that light's rim per art cell.
   The box tracks the rendered door transform and shadow-caster state. The
   fallback uses the existing composite and requires no global shadow atlas.
   HDR intensity controls coverage separately from hue, preserving colored edges.
   Use `DarkEdgeFill` on a marked light to add a small colored contribution to
   dark contour cells, with a faint inner wrap and a fade across the light's
   range. Its default value is zero. It fades out on bright source pixels and
   uses the existing composite pass and highlight limit.
   Where native lights overlap on the same art cell, their hues blend with
   greater weight on the stronger contribution. The brightest contribution
   bounds that cell's response, so coincident lights do not wash its color out.
   Enable realtime shadows on a marked point or spot light to let existing
   shadow casters block the rim. The pass samples URP's additional-light shadow
   atlas at art-cell centres. The active renderer and quality tier must provide
   additional-light shadows with forward-compatible light indices.

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
limits and a weak inward light wrap. The rim uses the same native realtime
shadow visibility and distance fade as other additional-light receivers.

Native Point/Spot lights also resolve view depth. A light centered behind the
actor produces a symmetric silhouette band and inward transition; crossing its
projected center remains continuous. A centered front light does not produce
that backlight band. Source art stays point sampled and the body interior keeps
the profile's existing exposure and highlight protection.
The symmetric rear band is limited to one art cell. Its inward transition spans
at most one additional cell at 15% of the configured wrap strength; coherent directional
side lighting retains its own profile width and transition. The center projection
also fades the directional wrap continuously instead of flipping it across XY=0.
When marked lights arrive from opposed directions, their weighted directional
coherence also narrows the combined band and reduces its inner shell. Multiple
lights on the same side keep the directional profile instead of being dimmed
merely because more than one source is present.

Only registered actors visible to a game base camera update their silhouette.
Scene lights are captured from that camera's URP culling results. Disabling an
actor or removing its light removes its contribution in the same camera render.
Marked local lights are filtered once per camera. A camera with no eligible
native sources skips native actor preparation, while explicit fields remain available.
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
indices. The backend depends on `ZeroEngine.Render` 0.2.0 and URP 17.6; there is
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

## Pixel blood backend (0.2.3)

Pair this version with Render 0.3.6 at the same tested Git commit.
`BloodVisualResourcesLoader.Load(optionalAuthoredSpray)` loads the runtime surface-dye
and spatial pixel-blood shaders from `Resources/ZeroEngine/Blood`. They use URP Core,
receive caller-provided blood colours and preserve the source art's pixel scale.
No POB shader include or global setting is required. This backend does not add a renderer
feature or automatically enable gameplay effects. Runtime Resources assets keep both
shaders referenced in a Player; source sprites/templates remain consumer-owned.
