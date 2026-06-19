# Changelog

ZeroEngine is developed as a multi-package repository. Package versions are
tracked in each package's `package.json`.

## Unreleased

- Added the repository-wide package graduation audit baseline.
- Added package naming and ownership guidance for ZeroEngine reusable packages, ZGS service SDKs, and project adapter packages.
- Added CI package naming ownership validation for ZeroEngine, ZGS service, known-debt, and project-adapter naming lanes.
- Added CI package boundary validation for internal asmdef/package dependency drift and declared internal dependency version drift.
- Added CI package metadata validation for UPM manifest, author, keywords, README, README version consistency, and CHANGELOG completeness.
- Added CI consumer install documentation validation for pinned Git UPM guidance and package README dependency pinning notes.
- Added CI package shipping cleanliness validation for local agent/editor folders and backup-looking files inside UPM packages.
- Added CI Unity C#/asmdef `.meta` pair validation and package-wide `.meta` GUID uniqueness checks for package assets.
- Added CI Unity test assembly validation so package tests remain visible to Unity Test Runner.
- Added CI asmdef isolation validation for duplicate assembly names, Editor platform scoping, Runtime/test reference boundaries, GUID reference bans, and ZeroEngine/ZGS reference resolution.
- Added CI Unity package dependency validation for known Unity asmdef references.
- Added CI config workbench coverage validation for `CreateAssetMenu` configuration assets, package-local config validators, DataToolkit bridge compatibility, and direct validator test coverage.
- Updated Unity CI setup to copy every `com.zerogamestudio*` package and test every package with a `Tests` folder.
- Validated the package graduation branch on the ZGS build machine with Unity 2022.3.62f3: all static gates passed, package import/compile succeeded, and EditMode tests passed 510/510.
- Added DataToolkit config discovery, semantic validation, CSV export/import previews, reference graph inspection, batch editing, and Editor tests.
- Added a Narrative Quest editor assembly split and deeper quest config validation tests.
- Added an Economy editor assembly split and package-level config validation for items, recipes, loot tables, and shops.
- Added a Combat editor assembly split and package-level config validation for abilities, projectiles, and spawn data.
- Added a World editor assembly split and package-level config validation for calendar events, weather presets, and day/night presets.
- Added an RPG editor assembly split and package-level config validation for battle rewards, encounter tables, and skill visual data.
- Added a Gameplay editor assembly split and package-level config validation for interaction and tutorial assets.
- Fixed the Gameplay `WaitCommand` source filename so Unity imports it as C#.
- Added a Character editor assembly split and package-level config validation for equipment, jobs, martial arts, realms, sects, party formations, and talent trees.
- Added AI, Audio, Data, DLC, Network, Persistence, and Social editor assembly splits with package-level config validation for their package-owned config assets.
- Added package-level config validation for Analytics configs, UI view/toast settings, and umbrella Spine skin configs.
- Fixed package test asmdef references and validator test assertions found by remote Unity EditMode execution.
- Added package-level changelog baselines for every top-level package.
- Normalized first-pass internal package dependencies to match asmdef references.
- Normalized first-pass Unity package dependencies for core, gameplay, and UI asmdef references.
- Added Unity 2022.3-compatible Network package dependencies for Netcode, Transport, and UGS assemblies.
- Filled remaining package license/repository metadata gaps.
- Filled Analytics package author and keyword metadata gaps.
- Removed hard references to optional third-party assemblies from default AI, Core, Persistence, Narrative, and UI assemblies.
- Renamed umbrella ModSystem assemblies to avoid duplicate asmdef names with the split ModSystem package.
- Regenerated split ModSystem `.meta` GUIDs that duplicated umbrella package assets.
- Moved package-local `.claude` planning/spec files to repository-level docs.
- Removed stale optional third-party version defines from the default umbrella assembly.
- Fixed generated README text construction in ModCreator editor windows.
- Added root project documentation.
- Added contribution, support, and security guidance.
- Added MIT licensing.
- Normalized package repository metadata for UPM Git dependencies.

For package-specific changes, inspect the package README, package version, and
Git history for the relevant `com.zerogamestudio.*` directory.
