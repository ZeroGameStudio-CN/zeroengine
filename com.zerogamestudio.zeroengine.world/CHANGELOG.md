# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added this package-level changelog as the baseline for package graduation tracking.
- Added a World editor assembly and package-level config validation for calendar events, weather presets, and day/night presets.
- `MinimapController` now requires an assigned `FollowTarget` instead of searching the active scene for a `Player` tag at runtime.
- Added Editor tests for game-date arithmetic, clamping, and calendar event activation rules.
