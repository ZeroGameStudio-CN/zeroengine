# ZeroEngine ModSystem

ZeroEngine ModSystem provides project-neutral mod discovery, manifest reading, path safety, source registration, management projection, and safe importer orchestration.

Source discovery for production startup uses `IAsyncModSource` and
`ModLoadOrchestrator.LoadFromSourcesAsync`. Enabled sources start together and are
bounded by `ModLoadOptions.SourceQueryTimeout` (30 seconds by default). Results are
processed in source registration/input order and folder ordinal order, so callback
completion order cannot change load order. Timeout, cancellation, source failures, and
dependency import failures are reported with stable `ModLoadIssue.ReasonCode` values.

The callback member on `IModSource` and synchronous orchestrator methods are retained as
obsolete compatibility APIs for one package cycle. New integrations must implement
`IAsyncModSource`; projects must not use a later hot reload to compensate for incomplete
startup discovery.

## Assemblies

- `ZeroEngine.ModSystem`: neutral contracts for mod manifests, source discovery, safe path resolution, management projection/activation, and core loader orchestration with project-provided importers.
- `ZeroEngine.ModSystem.Legacy`: opt-in compatibility for older `$type` JSON object parsing, singleton loader compatibility, hot reload, and Lua hooks.
- `ZeroEngine.ModSystem.Steam`: optional, explicitly composed Steam Workshop client/source. It requires a project publish policy, a caller-supplied source ID and manifest filename, and never auto-registers itself.
- `ZeroEngine.ModSystem.Editor`: legacy editor tools for the older `$type` JSON workflow.

## Project Responsibilities

Projects own their content semantics. A project adapter decides how manifest-declared files map to cards, skills, weapons, quests, TCE graph IDs, localization tables, or runtime config.

ModSystem must not know project-specific concepts such as POB cards, weapons, buffs, rooms, save data, or text tables.

Projects implement `IModActivationStore` with atomic persistence: a `PersistenceFailed`
result must leave `DisabledModIds` unchanged. `ModManagementService` rebuilds read-only
snapshots only from the final startup report and that persisted store. Runtime source
changes set `IExternalModChangeSignal.RestartRequired`; they never hot-load content.

Steam publishing always requires an explicit `WorkshopVisibility` and an
`IModPublishPolicy`. A missing policy rejects every visibility. The adapter obtains its
App ID from Steam runtime or explicit constructor configuration and contains no project
App ID or project gate.
