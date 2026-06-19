# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added a dedicated `ZeroEngine.Economy.Editor` assembly for package editor tooling.
- Added package-level Economy config validation for item IDs/prices/stacks, crafting ingredients/outputs/unlocks, loot table entries, and shop prices/stock/schedules.
- Added direct Editor tests for Economy config validation behavior.
- Added this package-level changelog as the baseline for package graduation tracking.
- Added Editor tests for inventory slot stack clamping, merge behavior, and clone semantics.
