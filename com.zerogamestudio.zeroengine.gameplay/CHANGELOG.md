# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added this package-level changelog as the baseline for package graduation tracking.
- Added a Gameplay editor assembly and package-level config validation for interaction and tutorial assets.
- Declared the Input System and TextMeshPro dependencies used by the gameplay assembly.
- `InteractionPromptUI` now requires an assigned or injected `InteractionDetector` instead of searching the active scene at runtime.
- `TutorialManager` now uses an assigned or injected `Player` reference instead of searching the active scene by `Player` tag.
- Tutorial target resolution now uses registered targets or `TutorialUIManager` containers instead of `GameObject.Find` and Canvas scene scans.
- Added Editor tests for tutorial registered-target resolution.
- Fixed `WaitCommand` so the source file uses a valid `.cs` filename.
