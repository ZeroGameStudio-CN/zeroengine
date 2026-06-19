# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Declared the `com.zerogamestudio.zeroengine.persistence` package dependency used by the data assembly.
- `Stats.LoadFromData` now invalidates cached stat values when restoring saved base values.
- Added Editor tests for stat modifier math, cached load behavior, source-based modifier removal, and current-stat clamping.
- Added a dedicated Editor assembly and package-level config validation for buffs and math formulas.
