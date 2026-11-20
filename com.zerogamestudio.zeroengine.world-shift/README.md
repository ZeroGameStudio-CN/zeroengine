# ZeroEngine World Shift

World-space visual transitions between two caller-selected visual layers. The
package owns transition progress, participants, shader properties and its render
backend. Projects own world identity, gameplay state, scene loading, collision,
save data and the decision to start a transition.

`WorldShiftTransitionController` supports material reveal and the URP stencil
renderer feature. World Shift 0.1.2 requires Unity 6000.6 and URP 17.6.0.
The separate URP assembly uses Unity 6 Render Graph for its stencil pass.

The package preserves the original public types, serialized fields and asset
GUIDs. POB's project-specific participants and orchestration live in
`com.zerogamestudio.pob.world-shift`.

After verification and publication, install through
`https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.world-shift#<tested-commit>`.
Keep related ZE package pins on the same tested commit. Temporary local package
references are for verification only.

Run `ZeroEngine.WorldShift.Tests.Editor` in an isolated project to verify the
controller and renderer contracts. Project transitions additionally require their
own scene and visual acceptance before adoption.
