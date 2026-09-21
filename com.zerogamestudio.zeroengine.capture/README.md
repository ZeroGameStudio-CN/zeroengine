# ZeroEngine Capture

Editor-only background camera capture for Unity 2022.3 / Unity 6. No ZE gameplay,
Timeline, test-runner, CLI or network dependency. No Player assembly is shipped.
Compatibility is a validation target until the package spec records both runs.

```csharp
using ZeroEngine.Capture;

// In an existing isolated Editor PlayMode scenario, after camera readiness:
using (var capture = BackgroundCapture.Start(
    new CaptureOptions { OutputRoot = externalArtifactRoot },
    () => currentGameplayCamera,
    () => explicitOverlayCanvases))
{
    // Await the project's existing gameplay route and verify its assertions.
    capture.Complete();
}
```

Call on Unity's main thread. The session samples end-of-PostLateUpdate with
fixed simulation time (default 60 Hz, recording 30 fps). It does not wait for
Game-view presentation or change window focus. Unity still requires a functioning
interactive graphics device; this is not headless capture or performance data.
Project adapters own camera switching, scene/save isolation and gameplay input.
For a still game/panel image, call `BackgroundCapture.Screenshot(options, camera,
overlays)`. It uses the same camera path without changing capture timing. This
does not capture Editor windows or the SO Inspector; use the native Editor/UI
evidence route for those, and validate serialized references separately.
Only explicitly supplied root overlay canvases are temporarily rendered with the
camera. No scene search, business logic or production asset writes occur here.

`Complete()` publishes `capture.json` for the existing external media helper;
PNG names follow `<label>-0000.png`. Only call it after scenario success.
`Dispose`, cancellation, frame limit, reload or PlayMode exit retain incomplete
frames with `session.json`, never a success manifest. A timing discontinuity or
render/write exception fails the session and restores its borrowed state.
Default bound is 120 simulated seconds. Audio and automatic encoding/delivery
are deliberately outside this package. Do not classify recording FPS as game FPS.

Install from this repository's package path at a tested full Git commit, following
`docs/consumer-project-setup.md`. Local `file:` references are iteration only.
Add this package to the consumer's `testables` to run
`ZeroEngine.Capture.Tests.CaptureContractTests` (EditMode). Run a real isolated
PlayMode capture in each consuming project before promoting a version.
