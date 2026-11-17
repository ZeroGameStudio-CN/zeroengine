# ZeroEngine Cinematic Cinemachine

Optional Unity 6000.6+ integration for native Cinemachine Timeline shots. The base Cinematic package stays independent of Cinemachine.

Use a normal Timeline containing `CinemachineTrack` clips and native spline/animation tracks. Resolve every shot's `VirtualCamera.exposedName` through the existing registry's native PropertyName overload. `CinemachineTimelineScope` binds each camera track to an owner-scoped `ICameraOverrideStack` proxy; the native mixer still computes the cuts and blends. It does not create a second clock, interpolate camera poses, reset a Brain globally, or run gameplay.

For Cinematic playback, declare camera tracks with `new CinematicBindingRequirement("camera.timeline.override", cameraTrack, managedByProjectService: true)`. The project service creates the scope before playback. Cinematic 2.3 checks the actual generic binding after Enter and before start commands/graph playback. AnimationTrack bindings remain ordinary registry requirements. Shot references are rig bindings, not empty-track requirements.

The caller owns the rig and subjects. Supply a binding-lifetime predicate for pooled/reusable rigs and a camera-authority predicate for handoff. Call `Retire()` immediately when handing authority away or disabling/reusing a Brain, before a native lifecycle reset can reuse override IDs. Only this scope's IDs are released; action/light/audio cues may continue. `Dispose()` detaches its exact listener and restores still-owned bindings and virtual-camera enable state. It never restores a physical camera pose or changes Brain update settings; consumer adapters retain their return policy. LastAppliedPose observes the Brain's actual output for that policy.

Keep rig cameras disabled outside playback (and give them appropriate output channels/standby priorities). Use native AnimationTrack curves for `CinemachineSplineDolly`'s normalized position. Scene content, shot timing and target selection belong in consumer configuration.

A null Brain explicitly installs inert camera-track bindings for cue-only consumers. It leaves rig cameras disabled and records no camera output; the same Timeline can still animate its rail and execute non-camera cues.

Development `file:` references are temporary. Publish a tested Git commit and keep a consumer's ZE packages on its validated cohort before formal adoption.

Consumer setup uses both direct Git UPM dependencies at the same tested commit:

```json
{
  "com.zerogamestudio.zeroengine.cinematic": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=/com.zerogamestudio.zeroengine.cinematic#<tested-commit>",
  "com.zerogamestudio.zeroengine.cinematic.cinemachine": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=/com.zerogamestudio.zeroengine.cinematic.cinemachine#<tested-commit>"
}
```
