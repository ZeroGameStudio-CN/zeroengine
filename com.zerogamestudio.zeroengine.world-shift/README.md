# ZeroEngine World Shift

World-space visual transitions between two caller-selected visual layers. The
package owns transition progress, participants, shader properties and its render
backend. Projects own world identity, gameplay state, scene loading, collision,
save data and the decision to start a transition.

`WorldShiftTransitionController` supports material reveal and the URP stencil
renderer feature. The URP assembly is separate from the controller assembly;
this package currently declares URP as a required installation dependency. It
does not promise a Built-in or HDRP renderer. Unity 6 uses the Render Graph path;
older conditional code is retained but has not been verified in this audit.

The 0.1.1 consolidation candidate preserves the embedded POB package's public
types, serialized fields and asset GUIDs. Project-specific participants and
orchestration remain in `com.zerogamestudio.pob.world-shift`. Copying this source
into the ZE repository does not publish it or change POB's installed package.

After verification and publication, install through
`https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.world-shift#<tested-commit>`.
Keep related ZE package pins on the same tested commit. Temporary local package
references are for verification only.

Run `ZeroEngine.WorldShift.Tests.Editor` in an isolated project to verify the
controller and renderer contracts. Project transitions additionally require their
own scene and visual acceptance before adoption.
