# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added this package-level changelog as the baseline for package graduation tracking.
- Declared Addressables and Input System dependencies used by the UI assembly.
- Removed the hard Odin assembly reference from the default UI assembly.
- `UIManager` now requires prefab or Addressables references instead of loading view prefabs from `Resources`.
- `UIViewDatabase` editor auto-find no longer uses hard-coded `Assets/...` search paths.
- Toast runtime bootstrap no longer searches the active scene for a presenter.
- Added package-level config validation for UI view databases and toast settings.
