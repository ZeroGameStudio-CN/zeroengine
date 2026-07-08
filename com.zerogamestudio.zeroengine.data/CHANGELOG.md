# Changelog

## 3.1.0

### Added

- `BuffHandler.OverrideDuration(float duration)`: override this handler's duration at the point of application (e.g. the same `BuffData` asset applied with different durations by different skills/abilities). Subsequent `RefreshDuration()` calls use the overridden value instead of `Data.Duration` until a new override is set.

### Fixed

- `ModExporter.ExportBuff` (in `com.zerogamestudio.zeroengine`, the base package) still referenced the removed `BuffStatModifierConfig.StatType` field from the 3.0.0 `StatId` migration, which broke compilation of that package's Editor assembly for any project referencing it. Updated to export `StatId.Value`.

## 3.0.0

### Breaking

- `BuffStatModifierConfig.StatType` (enum) replaced with `BuffStatModifierConfig.StatId` (string-based `StatId`), aligning BuffSystem with the newer StatId-based StatSystem.
- `BuffHandler` constructor now takes `IBuffStatTarget` instead of the concrete `StatController`. `StatController` implements `IBuffStatTarget`; existing call sites passing a `StatController` continue to compile unchanged.

### Added

- `IBuffStatTarget` interface (`AddModifier`/`RemoveModifier` by `StatId`).
- `BuffHandler.RestoreState(float remainingTime, int stacks)` for save/load restoration without triggering stack-change lifecycle side effects. Guards against being called on an already-expired handler; warns (does not throw) on a negative `stacks` argument before clamping it to 0.

### Changed

- `BuffData.Duration` doc comment clarified: dimensionless time unit (seconds for real-time ticking, 1.0-per-turn for turn-based ticking); `0` still means permanent.
