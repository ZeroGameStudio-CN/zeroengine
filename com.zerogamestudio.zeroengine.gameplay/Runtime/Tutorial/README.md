# Tutorial system

Reusable tutorial sequences, scoped interaction guides and presentation services. Namespace: `ZeroEngine.Tutorial`; package: `com.zerogamestudio.zeroengine.gameplay`.

## Ownership and installation

Consumers install the official package at a tested commit:

```json
"com.zerogamestudio.zeroengine.gameplay": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.gameplay#<tested-commit>"
```

Gameplay depends on ZE Core/Persistence, uGUI, Input System, TextMeshPro and the built-in Video/Director/ScreenCapture modules. ScreenCapture is used by optional native-frame verification recordings. A consumer pins the ZE dependencies under its repository policy. Local file bindings are for validation only. Consumers select a verified Git commit from the official repository.

The package contains no POB, Pixel Crushers or Odin dependency. Quests, inventories, feature access, save-slot selection and authored level generation stay with the consuming game. Tutorial completion is not proof of a business objective: use native operation callbacks and project result events for that objective.

## Authoring

1. Create `TutorialConfigSO` for shared appearance, font, timing and media. Each media item has a stable ID; placeholders include a replacement note.
2. Create `TutorialSequenceSO`. Its Inspector has an **Add step** picker and **Validate** action. Steps have stable IDs, skip policy, prerequisites and sequential/parallel/any composition.
3. Add a `TutorialManager`, configure its profile, register the sequence and bind the game's targets/services. Call `StartSequence` explicitly, `CheckAutoStart` for configured conditions, or use `TutorialSignalTrigger` for scoped event requests.
4. Import **Guidance Playground** from Package Manager Samples. In an empty scene, add `TutorialPlayground` and enter Play. This sample does not read or write the game's saves.

Use InputSystemUIInputModule with Input System (or Both) input handling. The default view is created at runtime; prefabs are optional. Assign the project's TMP font, or import TextMeshPro Essential Resources for its default font. New canvases use the configured reference resolution and do not modify an existing canvas.

## Targets and scopes

Register stable IDs through `context.Focus.Targets.Register(id, gameObject)` and dispose the returned lease when its owner exits. A `TutorialTargetAnchor` can bind to a manager/context without a global scene search or implicit manager creation. A duplicate live ID is rejected; an old lease cannot remove a replacement registration.

Target references support:

| Kind | Resolution |
| --- | --- |
| Auto / UI | Registered object or path beneath ScopeRoot; UI regions may contain any native pointer handler |
| World | Collider/renderer bounds, or explicit world size, projected through WorldCamera |
| ScreenRegion | Normalized rectangle, independent of scene object ownership |

Paths use a complete relative path or an unambiguous name. With no explicit root, path lookup is limited to the active scene. Hidden, ambiguous, offscreen and behind-camera targets do not acquire a blocking surface. Missing targets wait for TargetWaitSeconds; losing a target releases the input surface while waiting again.

A Focus request can highlight several targets, with **Any** or **All** completion. `Interactive=false` creates a read-only highlight. `VisibleExclusions` exposes actual dialogue content; never supply a full-screen dialogue container as the exclusion.

Rectangle, circle and rounded rectangle masks support padding, feather and border style. Circle holes circumscribe target rectangles. A request supports fifteen targets plus one dialogue exclusion. Visual holes and input admission are separate: a visible dialogue hole does not make the controls underneath clickable.

## Native operations

| Interaction | Completion |
| --- | --- |
| Click | Genuine native pointer click / submit, exactly once |
| Hold | Continuous pointer or confirmation hold reaching HoldSeconds; early release/exit cancels the press |
| Drag | Native initialize/begin/drag/drop/end events, with a configured destination; keyboard/gamepad uses source confirmation then destination confirmation |
| EnterArea | Actor position enters the target world bounds |
| Signal | The context's specified project event is received |
| Display | Presentation duration only; it is not an operation objective |

The closest underlying UI/collider is checked, so an intervening control cannot be clicked through. Screen-region gestures do not activate an unrelated control underneath. Custom world operations can connect `TutorialInteractionRelay.OnActivated` to the project's real interaction method. For accepted inventory drops, purchases and other business rules, supply `AcceptInteraction` or a fresh context-local `CompletionEvent`.

`BlockOtherInput=false` observes the existing click/drag handlers instead of replaying their actions. The input gate snapshots UI maps and explicitly supplied project maps; only pointer, confirmation, navigation, cancellation and AllowedActions remain enabled. Provide `InputMaps`, `CanRestoreInputMap`, `IsValid` and `AcquireScope` for the game's player/input/time ownership. The package does not write a game's global time scale or fabricate gameplay events.

## Presentations

`PresentationStep` references a media ID; `FocusStep.PresentationId` attaches corner coaching to an operation. Normal dialogue is a separate beat with `DialogueLayout=Normal`; built-in choices require that layout. `ITutorialPresentationPlayer` / `ITutorialPresentation` support Text/Dialogue, CG, Video, Timeline and custom backends.

The built-in player provides speaker/text/portrait, simple choices, CG timing, VideoPlayer first-frame readiness and unscaled playback, and PlayableDirector bindings by stream name/registered target ID. `IsReady` for video requires a real decoded frame. A completed/skipped handle keeps its frame until disposed, allowing a project-owned scene preparation barrier to release it at the right time. Skipping pauses video/Timeline and submits only a Skipped result. Permanent rewards never run from Timeline callbacks.

Register a dialogue backend by key with `context.Presentations.Register(key, player)`. A consuming game may continue using its existing dialogue UI and conversation database. A handle must stop only its own playback, cancel on stale scene/slot ownership and dispose its resources. Built-in resources are released with the handle; failed or late callbacks cannot overwrite its terminal result.

## Conditions, progress and project outcomes

Timing defaults to the injected realtime clock, including paused UI. UseUnscaledTime is configurable for other tutorial steps. Operation steps reject automatic completion delays. Input/event/movement timeouts fail rather than count as learning.

Disable, cancel, failure and destruction exit each entered step exactly once. Skips emit OnStepSkipped, not OnStepCompleted. Parallel composition permits one blocking presenter; put corner coaching on FocusStep. Stable step IDs are saved with the current index; reordering resolves the ID first. First-time markers survive export/import.

Level and quest conditions require `QueryState` / `QueryQuestState`; an unavailable provider remains unsatisfied. World/minimap markers use `CreateWorldMarker` and an owned disposable lease. Completion rewards require `ITutorialRewardService`: it grants the complete batch atomically and idempotently by sequence ID. Rejected or unavailable rewards do not record sequence completion. A tutorial never bootstraps a save manager implicitly.

Natural trigger requests are deduplicated, prioritized and revalidated before starting. Disabling/unloading their source cancels its pending request. An expired source does not interrupt an active lesson or force itself into another scene.

## Verification

The package includes focused Editor contracts and a standalone PlayMode scenario covering native UI input, multiple targets, hold, pointer/two-confirm drag/drop, context events, world/area targets, media and owned cleanup. Captures use native ScreenCapture after WaitForEndOfFrame and actual realtime timestamps.

Consumer startup/quit callbacks also run for package PlayMode tests. Use the consuming project's prebuild storage guard and isolated fixture when those callbacks can write player data. Restore normal dependency and metadata bytes after temporary local validation. The runtime scenario accepts a readable source `FontFixture` and optional `VideoFixture`; it never changes those imported assets.
