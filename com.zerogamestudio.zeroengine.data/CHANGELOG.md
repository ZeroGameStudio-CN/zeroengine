# Changelog

## 3.0.0

### Breaking

- `BuffStatModifierConfig.StatType` (enum) replaced with `BuffStatModifierConfig.StatId` (string-based `StatId`), aligning BuffSystem with the newer StatId-based StatSystem.
- `BuffHandler` constructor now takes `IBuffStatTarget` instead of the concrete `StatController`. `StatController` implements `IBuffStatTarget`; existing call sites passing a `StatController` continue to compile unchanged.

### Added

- `IBuffStatTarget` interface (`AddModifier`/`RemoveModifier` by `StatId`).
- `BuffHandler.RestoreState(float remainingTime, int stacks)` for save/load restoration without triggering stack-change lifecycle side effects. Guards against being called on an already-expired handler; warns (does not throw) on a negative `stacks` argument before clamping it to 0.

### Changed

- `BuffData.Duration` doc comment clarified: dimensionless time unit (seconds for real-time ticking, 1.0-per-turn for turn-based ticking); `0` still means permanent.
