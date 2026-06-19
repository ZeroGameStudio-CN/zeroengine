# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Declared the `com.zerogamestudio.zeroengine.economy` package dependency used by the character assembly.
- `PartyManager` now uses its assigned `PartyConfigSO` instead of loading `Resources/PartyConfig` at runtime.
- Added Editor tests for party slot member assignment, clearing, locking, and swaps.
- Added a dedicated Editor assembly and package-level config validation for equipment, jobs, martial arts, realms, sects, party formations, and talent trees.
