# ZeroEngine.Cinematic

Reusable cinematic runtime contracts and validation for Timeline-backed authored performances.

This package intentionally contains project-agnostic playback policy, sequence catalogs, binding resolution, watchdog timeout handling, and authoring validation. Downstream projects provide adapters for input, camera, lifecycle events, narrative commands, UI, save, battle, quest, and other business systems.

## Runtime Responsibilities

- `CinematicSequenceDefinition`: authored Timeline reference, binding requirements, command list, skip policy, input-lock policy, camera-restore policy, minimum playback time, and timeout policy.
- `CinematicSequenceCatalog`: reusable lookup and validation surface for authored sequences.
- `CinematicBindingRegistry` and `CinematicBindingSource`: project-agnostic binding registration, resolution, and live binding-key validation for Timeline tracks.
- `CinematicPlayableDirectorAdapter`: low-level PlayableDirector orchestration, lifecycle service entry/exit, command phase execution, binding application, timeout stop, and terminal cleanup.
- `CinematicPlaybackService`: sequence-resolving playback facade with active-playback state, interrupt policy, skip policy, tick-based completion, and timeout evaluation.
- `CinematicPlayResult`: normalized playback result with success classification for completed and skipped-completed terminal statuses.
- `CinematicStableId`: shared lowercase semantic stable-id rule for runtime and editor validation.
- `CinematicValidationKernel`: editor validation for missing timelines, invalid bindings, duplicate ids, empty command ids, invalid timing, and catalog integrity.

## Downstream Project Responsibilities

Projects using this package should keep all business-specific behavior outside the package:

- Input lock and restore implementations.
- Camera restore implementation.
- Event projection to project event buses.
- Narrative, quest, battle, save, UI, and analytics command execution.
- Scene, prefab, Addressables, or content-specific catalogs.

Project adapters should depend inward on `ZeroEngine.Cinematic` through `ICinematicProjectPlaybackService`, `ICinematicCommandExecutor`, and `ICinematicSequenceResolver`.

## Playback Flow

1. Resolve a `CinematicSequenceDefinition` by sequence id.
2. Build a `CinematicPlayRequest` from the resolved sequence so authored policies are authoritative.
3. Enter project playback services.
4. Execute `OnStart` commands.
5. Bind required Timeline tracks and start the `PlayableDirector`.
6. Tick the playback service until completion, timeout, skip, cancel, or abort.
7. Execute the matching terminal command phase and exit project playback services in reverse order.

`CinematicPlayableDirectorAdapter` rejects mismatched request and sequence ids to prevent lifecycle services from observing one sequence id while the director plays another sequence asset.

Stable ids are normalized at runtime: sequence ids, binding keys, request/source ids, result sequence ids, and command ids trim surrounding whitespace before lookup or command dispatch. Authoring validation and live binding-registry validation report invalid stable ids that are not lowercase ASCII semantic ids using `a-z`, `0-9`, `.`, `_`, and `-` with an alphanumeric first and last character.

`CinematicBindingSource.SetRegistry` lets downstream projects inject the intended `CinematicBindingRegistryBehaviour` before activating runtime prefab instances. The `OnEnable` scene lookup remains a compatibility fallback, not the preferred production wiring path for spawned content.

`CinematicSkipPolicy.Abort` treats an authored skip request as an aborted terminal path: it runs `OnAbort` commands and exits project playback services with `CinematicPlayStatus.Aborted`.

`CinematicSkipPolicy.Always` skips immediately and does not wait for `MinimumPlaybackSeconds`. Use `AllowAfterMinimumPlayback` only when authored content requires a protected opening window before skip completion.

Skipped-completed playback executes explicit `OnSkipped` commands when present. If no `OnSkipped` commands are authored, it falls back to `OnComplete` commands so generic completion side effects still run on skip completion.

Natural completion still respects `MinimumPlaybackSeconds`: if the `PlayableDirector` stops before the protected opening window has elapsed, `CinematicPlaybackService.Tick` keeps the playback active until the minimum duration is reached. A Timeline that reaches `PlayableDirector.duration` is also treated as naturally complete once the minimum window has elapsed, even if the director still reports `Playing`. `MinimumPlaybackSeconds` is clamped to at least `0.01f` so invalid authored values cannot complete a stopped Timeline in the same frame.

`CinematicCameraRestorePolicy.LeaveTimelineCamera` is an explicit no-restore authoring policy for Timeline-managed camera changes. Downstream camera services should capture and restore camera state only for `RestorePrevious`; `ApplyNamedState` remains unsupported by the generic validation kernel.

`CinematicSequenceCatalog.TryResolve` refuses duplicate sequence ids instead of selecting the first ambiguous asset. Run catalog validation during authoring to surface the duplicate id before play requests reach runtime.

## Verification

Run the package EditMode fixtures through Unity Test Runner or Unity MCP:

- `ZeroEngine.Cinematic.Tests.CinematicBindingRegistryTests`
- `ZeroEngine.Cinematic.Tests.CinematicPlayableDirectorAdapterTests`
- `ZeroEngine.Cinematic.Tests.CinematicPlaybackServiceTests`
- `ZeroEngine.Cinematic.Tests.CinematicPlaybackWatchdogTests`
- `ZeroEngine.Cinematic.Tests.CinematicPlayRequestTests`
- `ZeroEngine.Cinematic.Tests.CinematicPlayResultTests`
- `ZeroEngine.Cinematic.Tests.CinematicSequenceCatalogTests`
- `ZeroEngine.Cinematic.Tests.CinematicValidationKernelTests`

For MCP-based validation, confirm the selected Unity instance is the intended ZeroEngine editor and that each run reports `total > 0` with `failed=0`.
