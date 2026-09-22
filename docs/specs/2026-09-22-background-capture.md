# Background Unity capture

## Agreed boundary

Independent `com.zerogamestudio.zeroengine.capture` UPM package; P5 and POB
consume it without copying capture source. Initial target is interactive Editor
PlayMode on Unity 2022.3 and Unity 6, including an unfocused Game view. This is
not headless, audio, Player capture or performance measurement.

Editor-only assembly owns frame readback, bounded fixed-step sessions, output
manifest and restoration. No dependencies on ZE gameplay, Timeline, UniTask,
test runners, CLI/Pipeline/Uloop, Feishu or project types. The project supplies
camera selection and optional overlay canvases; existing isolated scenarios own
gameplay, save state and assertions. External media tooling encodes and sends.

Use synchronous camera rendering/readback first, matching the verified P5
capture approach. Avoid WaitForEndOfFrame and Game-view focus requirements.
Install a removable end-of-PostLateUpdate callback, sample integral simulation
frame intervals, and abort on timing changes instead of claiming continuous
footage. One session owns capture timing; bounded duration/frame count prevents
unattended accumulation. Stop, cancellation, exception, PlayMode exit and domain
reload restore state. Only explicit successful completion emits capture.json;
failed/aborted sessions retain frames and a diagnostic session.json.

## Implementation plan

1. Add package, Editor API, independent permanent contract tests and README.
2. Compile and run synthetic render/lifecycle tests in both existing Editors;
   inspect captured pixels and restoration, not only source assertions.
3. Add thin consumer integration through existing tools/tests, update each Atlas,
   and verify a real P5 route plus a POB gameplay capture. Do not introduce a new
   gameplay driver or mutate production configuration in tests.
4. Publish only the reviewed ZE scope under the established package contract;
   resolve pinned consumers and verify package/Atlas gates. No Plastic checkin
   without separate submission authority. Preserve existing unrelated pins/work.

## Acceptance and recovery

Both consumers record through the installed package with no per-record source
edit. Valid dimensions, nonblank moving frames, integral fixed-step cadence,
and a media-helper-compatible manifest. Camera target, active RT, canvas state,
capture timestep and background setting return to their previous values.
Cancel, renderer error, PlayMode exit, reload, duplicate start and invalid output
must fail closed without a success manifest or leaked callback/native resources.

Remove the new dependency and thin adapters to roll back; never rewrite existing
recordings or unrelated ZE pins. Test artifacts stay outside source workspaces.

## Status

Remote implementation commit: `353be899da13086fe27e01527fc45fa20358dd87`.
P5 and POB manifest/Unity-generated lock both resolve the remote capture package;
neither depends on the development checkout. P5's 30 ZE pins share this snapshot
(the former snapshot plus this package). POB retains its existing deliberate
version splits: this independent Editor leaf has no sibling ZE dependencies.

Validation on 2026-09-22:
- Unity 6.3 / P5: capture EditMode 9/9, PlayMode 2/2, real city route 1/1;
  native NUnit XML checked. Atlas coverage/feature/registry passed, freshness
  passed after regenerating for the final pins. Routed Editor console had no
  current compiler errors. Protected configuration SHA256 baseline: 895 unchanged.
- Unity 2022.3 / POB: compile succeeded, capture EditMode 9/9 + Atlas 3/3,
  PlayMode 2/2, real Intro/StartGame/BaseShip screenshot + video proof 1/1.
  Native Uloop terminal results were nonempty with no failures/skips. Protected
  configuration baseline: 713 unchanged; isolated world disposed before restoring
  the original save root. The one-shot integration fixture was removed, with an
  external reproducibility copy retained; cleanup compile passed with zero errors
  and zero warnings.
- P5 produced 1967 continuous 1280x720 frames at 30 FPS (60 Hz simulation),
  exported as 65.588 seconds. POB produced a standalone screenshot and 90 frames,
  exported as 3.021 seconds. Inspected actual images and sampled contact sheets;
  human motion/visual acceptance, audio and Player performance are not claimed.
- External task artifacts: `ze-capture-p5-20260922` and `ze-capture-pob-20260922`.
  Original frames remain under per-run OS temporary artifact directories.

User approved controlled manifest editing and then requested a durable remote
package route. Router pinned Git installation support was released as
78a234ab9522048a35f849cb1478c83a441d839d on Windows and macOS, with scoped tests.
Media helper now accepts a bounded 120-second continuous frame input (12 tests
passed on both development machines); skill guidance prioritizes the installed
capture package. Final skill release `8204aacd305f483ba51469d41ceee8fd57b51cf1`
is installed and read back on Windows and M5.
Game-camera screenshots share the writer; SO Inspector/Editor-window screenshots
remain a separate native UI evidence route, not a claim of this package.
No Plastic changeset was created. Package source is on the dedicated remote
`codex/unity-background-capture-20260922` branch; it is not merged into main.

## P5 visual-graduation follow-up: camera visibility fidelity

The P5 battle camera excludes the exploration world. Temporarily adding the
overlay Canvas layer to its culling mask breaks that boundary when the Canvas is
on Default. Correct this inside the shared writer, not with P5 renderer hiding.
Synthetic pixel tests must simultaneously prove visible green UI and a blue
background in place of an excluded red world quad; restoration checks remain in
force. Verify a real P5 battle after package
resolution before making art decisions from that evidence.

The user authorized publishing this fix on the existing independent branch and
updating P5 only. POB stays at its previously verified pin; no main-branch merge
or Plastic checkin is included. Candidate validation is pending.

The first candidate's pixel regression passed 9/11: preserving the camera mask
alone also excluded the UI. The follow-up borrows a layer already included by the
source camera for explicitly supplied Canvas/CanvasRenderer objects, restoring
their layers synchronously on success or failure. It never searches or suppresses
world renderers, widens the camera mask, or writes project assets. A zero-mask
camera with nonempty UI fails explicitly; the caller must choose a visible camera
layer. Synthetic coverage includes the high bit and write-failure restoration.
