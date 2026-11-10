# ZeroEngine Fog Of War URP

Optional rendering backend for `com.zerogamestudio.zeroengine.fogofwar` on Unity
6000.6 / URP 17.6. It owns its runtime fan mesh, material, command buffer and render
textures. Camera, sight and terrain meshes are borrowed and stay caller-owned.
There is no actor discovery, gameplay state, save access or render-feature installation.

Install both fog packages from the same tested upstream commit:

```json
"com.zerogamestudio.zeroengine.fogofwar": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.fogofwar#<tested-commit>",
"com.zerogamestudio.zeroengine.fogofwar.urp": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.fogofwar.urp#<tested-commit>"
```

Create `SightMaskRenderer2D` with a serialized reference to the package's
`Runtime/Shaders/SightMask.shader` (or a compatible four-pass shader). Keep the
shader referenced in builds. `RenderIfChanged(camera, sight, terrain, revision, z,
softnessPixels)` skips identical published inputs. Advance the terrain revision
whenever mesh contents change, including same-count edits; sight revision advances
on `Rebuild`. A changing camera matrix/size and lost targets invalidate reuse.

`Texture` contains raw current sight in R and terrain in G. `PresentationTexture`
contains softened sight in R when softness is positive. Texture dimensions follow
the camera; supported platforms use RG8/R8 with an ARGB32 fallback. Softness affects
presentation only. Fan topology is reused when its vertex count is unchanged;
positions and bounds are uploaded each render. Dispose the renderer when its owner
ends. Recreate after disposal.

This package supplies masks, not a particular game's atmospheric style/minimap UI.
The consumer composites them with its own art and explored-memory texture. Android,
WebGL and Linux are supported code paths; platform-specific runtime performance
requires measurement on those targets.

Version 0.2.5 pairs with the base fog package 0.2.5. Mask rendering and shader
passes are unchanged; the base package adds opt-in prepared sight reuse and
caller-owned ordered spatial segment queries. Logical visibility and rendering
ownership remain separate.

0.2.5 pairs with the base package activation-only observation API; URP drawing and shader behavior are unchanged.
