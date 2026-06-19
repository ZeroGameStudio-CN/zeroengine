# Changelog

All notable changes to this package will be documented in this file.

## Unreleased

### Changed

- Added a dedicated `ZeroEngine.Narrative.Editor` assembly for package editor tooling.
- Expanded Quest config validation for duplicate quest IDs, empty designer names, invalid conditions, invalid accept requirements, and invalid rewards.
- Exposed Quest config validation issues through a generic `Asset` property so shared validation reports can map issues back to quest assets.
- Added direct Editor tests for Quest config validation behavior.
- Declared the `data`, `economy`, and `gameplay` package dependencies used by the narrative assembly.
- Removed hard XNode and Steamworks assembly references from the default narrative assembly.
- `QuestManager` now loads configs only from explicit sources or registrations instead of `Resources/Quests`.
- `DialogBoxUI` now uses explicit portrait bindings or `RegisterPortrait` instead of `Resources.Load`.
