# ZeroEngine ModSystem

ZeroEngine ModSystem provides project-neutral mod discovery, manifest reading, path safety, source registration, and safe importer orchestration.

## Assemblies

- `ZeroEngine.ModSystem`: neutral contracts for mod manifests, source discovery, safe path resolution, and core loader orchestration with project-provided importers.
- `ZeroEngine.ModSystem.Legacy`: opt-in compatibility for older `$type` JSON object parsing, singleton loader compatibility, hot reload, and Lua hooks.
- `ZeroEngine.ModSystem.Steam`: optional Steam Workshop source adapter.
- `ZeroEngine.ModSystem.Editor`: legacy editor tools for the older `$type` JSON workflow.

## Dependencies

- `com.unity.nuget.newtonsoft-json` for manifest and legacy JSON parsing.
- The legacy compatibility and Steam assemblies are gated by define constraints.
  Projects that opt into those symbols must provide their referenced packages.

## Project Responsibilities

Projects own their content semantics. A project adapter decides how manifest-declared files map to cards, skills, weapons, quests, TCE graph IDs, localization tables, or runtime config.

ModSystem must not know project-specific concepts such as POB cards, weapons, buffs, rooms, save data, or text tables.
