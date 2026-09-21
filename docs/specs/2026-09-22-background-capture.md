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

Implementation in progress. POB local package installed with Client.Add and first
Unity 2022 compile passed. Focused tests and P5 validation remain pending.
User approved controlled manifest editing and then requested a durable remote
package route. Router pinned Git installation support was released as
78a234ab9522048a35f849cb1478c83a441d839d on Windows and macOS, with scoped tests.
Game-camera screenshots share the writer; SO Inspector/Editor-window screenshots
remain a separate native UI evidence route, not a claim of this package.
