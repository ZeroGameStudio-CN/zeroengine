# ZeroEngine Package Graduation Audit

Date: 2026-06-18

Scope: all top-level `com.zerogamestudio.*` Unity Package Manager packages in
this repository.

## Status

This is the first repository-wide graduation baseline and the current branch
has completed the package merge-readiness pass for all top-level packages. The
audit defines the production bar, records the blockers found by local static
checks, and records the remediation and verification evidence for this batch.

From a validation standpoint, this branch is ready to merge to `main` after the
normal repository review, commit, push, and CI process. This audit did not
commit, push, or merge the branch.

Claude Code was requested as the second reviewer. The local Claude CLI is
installed (`2.1.181`). Broad review attempts timed out before producing output,
including a 180-second read-only DataToolkit review attempt and a 120-second
merge-risk review attempt. A final narrow merge-risk prompt completed and
agreed that the static gates are mergeable once a Unity CI or build-machine
EditMode/PlayMode run confirms the behavior that local static checks cannot
cover. That confirmation has now been completed on the ZGS build machine with
Unity 2022.3.62f3. Keep future Claude participation split into small package
groups.

## Graduation Bar

A ZeroEngine package graduates only when all items below are true.

### UPM Package Contract

- `package.json` has correct `name`, `version`, `displayName`, `description`,
  `unity`, `license`, `repository`, `keywords`, and `author`.
- Runtime, Editor, and Tests assemblies are split by asmdef and platform.
- Every direct asmdef reference to another ZeroEngine package is declared in
  `package.json` with the current tested package version.
- Optional integrations are either declared as package dependencies or isolated
  behind optional asmdefs that do not compile unless their dependency exists.
- Each package has README and CHANGELOG coverage.
- Consumer install docs never require copying package folders into `Assets`;
  Git UPM dependencies stay pinned to tested commits.

### Runtime API

- Runtime code has no hidden project assumptions such as hard-coded
  `Assets/...` paths, project-specific namespaces, or consumer-scene object
  names.
- Reusable runtime systems do not rely on `FindObjectOfType`,
  `GameObject.Find`, tag searches, or `Resources.Load` as their primary
  dependency mechanism.
- Public APIs have deterministic error behavior and explicit null/empty input
  contracts.
- Save, async, addressable, network, and platform integrations are injectable
  or guarded so the package remains usable in minimal projects.
- Package tests cover the public contracts that consumers depend on.

### Designer Config Authoring

Configuration-heavy packages graduate only when designers can work without
manual asset spelunking or programmer-only validation.

- Config assets have stable IDs, display names, descriptions, categories, and
  ownership fields where relevant.
- Editors support search, filters, type/category grouping, batch edits,
  duplicate detection, missing-reference detection, and validation summaries.
- Editors provide Undo/Redo, dirty-state handling, save/apply semantics, and
  clear error messages that name the asset and field.
- Import/export flows use designer-friendly formats where applicable
  (CSV/JSON/table data), with dry-run diff previews before applying changes.
- Cross-config references can be inspected from both sides, and unresolved
  references are reported before play mode or build time.
- Validation has Editor tests so broken configs fail in automation, not in a
  designer's local scene.

## Findings And Fixes

### Missing Package Change Logs

This branch adds package-level CHANGELOG files for every top-level package.
Several packages still need richer historical entries before a public release,
but the package-level release tracking file now exists everywhere.

### Package Metadata

All top-level packages now pass `tools/validate-package-metadata.ps1`, which
checks UPM manifest identity fields, license, repository metadata, author,
keywords, README, explicit README version consistency, CHANGELOG, an
`Unreleased` changelog section, and `package.json.meta`. This pass also fills
the remaining Analytics author/keyword metadata gap and aligns its README
version text with `package.json`.

### Unity Meta Pairing

`tools/validate-unity-meta-pairs.ps1` now fails CI when package C# or asmdef
assets are missing Unity `.meta` files, or when `.cs.meta` / `.asmdef.meta`
files no longer have matching assets. This pass also adds missing `.meta` files
for umbrella package sample scripts and an Editor test, and removes a stale
sample script `.meta` whose source file no longer exists. The same gate now
checks all package `.meta` files for a valid GUID and rejects duplicate GUIDs
across packages; the split `modsystem` package had 19 `.meta` GUIDs duplicated
from the umbrella ModSystem assets, and this branch regenerates the split
package side while preserving the umbrella GUIDs.

### Consumer Install Documentation

`tools/validate-consumer-install-docs.ps1` now checks consumer-facing docs for
moving ZeroEngine Git UPM branch pins such as `#main` and stale
`Assets/ZeroEngine` package paths. Consumer setup docs continue to allow
`file:` only as an explicitly temporary local debugging option before handoff.
For packages that declare `com.zerogamestudio.*` dependencies, the same gate
now requires package READMEs to document same-tested-commit dependency pinning
and link the standard Consumer Project Setup guide.

### Unity Package Dependencies

Known Unity package assembly references now pass
`tools/validate-unity-package-dependencies.ps1`. This covers Addressables,
Input System, Localization, TextMeshPro, Netcode, Transport, and UGS Core,
Authentication, Lobby, and Relay assemblies.

### Packages Without Tests

All top-level packages now have tracked test files and Unity-visible test
assemblies. The repository no longer has a zero-test package, and the generated
package smoke project now executes the package EditMode tests successfully.

`tools/validate-test-assemblies.ps1` now requires every package to keep a Tests
directory with real NUnit/Unity test attributes, checks every package test
asmdef for Unity Test Runner references or the legacy `TestAssemblies` marker,
and requires Editor tests to be Editor-only. This pass also fixes the DLC
Editor test asmdef so those tests are visible to Unity's runner.

### Config-Heavy Package Editor Tooling

All package-owned `CreateAssetMenu` configuration assets found by the current
static coverage scan now have a package-local Editor validator and direct
Editor test coverage. `input` and `localization` remain on the broader
designer-tooling watch list, but the current scan finds only documentation
examples, not package-owned runtime `CreateAssetMenu` assets, so they are not
blocked in this batch.

`com.zerogamestudio.zeroengine.data-toolkit` is now the shared config workbench
foundation instead of building unrelated editors per package. The foundation
pass now discovers explicit `ManageableData`,
`CreateAssetMenu`, and ZeroEngine/ZGS config-like ScriptableObject types, runs
generic stable-ID, broken-reference, display-text, numeric-range, Min/Max, and
empty-reference-list validation, shows validation state in the window, exports
CSV summaries, previews field-level CSV imports, applies supported
string/int/bool/float/enum field updates with Undo and dirty-save semantics,
batch edits one serialized field across visible assets, inspects
outgoing/incoming object references, and accepts package-specific validation
providers through `IDataToolkitValidationProvider`. The default profile also
adapts ZeroEngine/ZGS package-local static
`*ConfigValidator.Validate(IEnumerable<TConfig>...)` rules into the shared
report without forcing every business package to depend on DataToolkit.

`com.zerogamestudio.zeroengine.narrative` now has a dedicated
`ZeroEngine.Narrative.Editor` assembly and direct Editor tests for Quest config
validation. The Quest validator covers duplicate quest IDs, missing designer
names, invalid conditions, invalid accept requirements, and invalid rewards.
Additional Dialog and Achievement semantic validation remains useful follow-up
hardening, but it is not a blocker for this package-graduation batch.

`com.zerogamestudio.zeroengine.economy` now has a dedicated
`ZeroEngine.Economy.Editor` assembly and direct Editor tests for package-level
config validation. The Economy validator covers item IDs/prices/stacks,
crafting ingredients/outputs/unlocks, loot table entries, pity settings, shop
prices, stock, schedules, and duplicate IDs. Remote Unity Editor test execution
is covered by the validation evidence below, and designer-facing workflows are
covered through the shared DataToolkit workbench plus package-local validator
messages.

`com.zerogamestudio.zeroengine.combat` now has a dedicated
`ZeroEngine.Combat.Editor` assembly and direct Editor tests for package-level
config validation. The Combat validator covers ability names and TCE component
parameters, projectile IDs/prefabs/trajectory/combat numbers, spawn data
intervals, ranges, entries, weights, quantities, scale settings, and duplicate
keys. Remote Unity Editor test execution is covered by the validation evidence
below, and designer-facing workflows are covered through the shared DataToolkit
workbench plus package-local validator messages.

`com.zerogamestudio.zeroengine.world` now has a dedicated
`ZeroEngine.World.Editor` assembly and direct Editor tests for package-level
config validation. The World validator covers calendar event IDs, dates, times,
recurrence, weather preset types, fog/lighting/audio ranges, day/night curves,
gradients, sun angles, skybox assignment, and duplicate keys. Remote Unity
Editor test execution is covered by the validation evidence below, and
designer-facing workflows are covered through the shared DataToolkit workbench
plus package-local validator messages.

`com.zerogamestudio.zeroengine.rpg` now has a dedicated `ZeroEngine.RPG.Editor`
assembly and direct Editor tests for package-level config validation on the
default compile path. The RPG validator covers battle reward ratios and
multipliers, encounter table IDs/rates/entries/level gates, and skill visual
timeline/event parameters. Optional Spine/Timeline config surfaces remain behind
symbols and should be validated when those optional symbols are enabled; they
are excluded from the default no-symbol package smoke path.

`com.zerogamestudio.zeroengine.gameplay` now has a dedicated
`ZeroEngine.Gameplay.Editor` assembly and direct Editor tests for package-level
config validation. The Gameplay validator covers interaction detection/input/UI
hint settings, tutorial UI/timing/audio settings, tutorial sequence IDs,
polymorphic tutorial steps, conditions, rewards, standalone tutorial step
assets, tutorial prerequisites, and tutorial group references. Remote Unity
Editor test execution is covered by the validation evidence below, and
designer-facing workflows are covered through the shared DataToolkit workbench
plus package-local validator messages.

`com.zerogamestudio.zeroengine.character` now has a dedicated
`ZeroEngine.Character.Editor` assembly and direct Editor tests for package-level
config validation. The Character validator covers equipment data, equipment
sets, slot types, jobs, job skills, passives, job databases, martial arts,
martial art databases, realms, realm databases, sects, sect databases, party
configs, formations, talent nodes, and talent trees. Remote Unity Editor test
execution is covered by the validation evidence below, and designer-facing
workflows are covered through the shared DataToolkit workbench plus
package-local validator messages.

The remaining package-owned config assets now have first-pass package-level
validators as well: Analytics covers analytics configs; AI covers NPC
schedules, schedule presets, and behavior tree assets; Audio covers audio cues
and music tracks; Data covers buffs and math formulas; DLC covers content pack
catalogs; Network covers server configs; Persistence covers settings
definitions; Social covers relationship data and relationship groups; UI covers
view databases and toast settings; and the umbrella package covers Spine skin
configs. Remote Unity Editor test execution is covered by the validation
evidence below, and designer-facing workflows are covered through the shared
DataToolkit workbench plus package-local validator messages.

### Internal Dependency Mismatches

The initial static asmdef-to-package check found package metadata that did not
match actual assembly references. The first batch has been normalized in
`package.json` and package README files for:

- Umbrella `com.zerogamestudio.zeroengine`.
- `character`.
- `combat`.
- `dashboard`.
- `data`.
- `narrative`.
- `pathfinding2d`.
- `social`.

Keep this audit as a release gate so future asmdef changes cannot silently
break UPM metadata. `tools/validate-package-boundaries.ps1` now also checks
that every declared `com.zerogamestudio.*` dependency exists in the repository
and matches the local package version, even when the dependency is not reached
through a direct asmdef reference. It runs before Unity tests in CI.

### Package Shipping Cleanliness

`tools/validate-package-shipping-cleanliness.ps1` now rejects local
agent/editor workspace directories such as `.claude`, `.codex`, `.vscode`, and
backup-looking file names inside UPM package directories. This pass moves two
package-local `.claude` planning/spec files into repository-level `docs/` and
renames `WaitCommand.cs,` to `WaitCommand.cs` with its `.meta` preserved so
Unity imports it as a C# source file.

### Duplicate Assembly Names

The umbrella package no longer duplicates split `modsystem` asmdef names. The
umbrella-local assemblies are named `ZeroEngine.Umbrella.ModSystem` and
`ZeroEngine.Umbrella.ModSystem.Editor`, while the split package keeps
`ZeroEngine.ModSystem` and `ZeroEngine.ModSystem.Editor`.

`tools/validate-asmdef-isolation.ps1` now rejects duplicate asmdef names,
production Editor asmdefs that are not Editor-only, production Runtime asmdefs
that are Editor-only, production asmdefs that reference test assemblies, and
Runtime asmdefs that reference Editor assemblies. It also rejects GUID-based
asmdef references and requires every `ZeroEngine*` or `ZGS*` assembly reference
to resolve to a package asmdef in this repository. This runs in CI before Unity
package import and test execution.

### Optional Dependency Hard References

Several packages describe integrations as optional. The default assemblies no
longer hard-reference these third-party assemblies, so the packages graduate on
the default no-symbol package path. The optional integrations remain explicit
follow-up surfaces when a consumer enables the matching symbols:

- `core`: UniTask and Odin adapter assembly.
- `ai`: CrashKonijn GOAP adapter assembly.
- `persistence`: Easy Save 3 adapter assembly.
- `narrative`: XNode and Steamworks adapter assemblies.
- `ui`: Odin adapter assembly.

Known Unity package dependencies are now declared and validated by CI for
Addressables, Input System, Localization, TextMeshPro, Netcode, Transport, and
UGS Core/Auth/Lobby/Relay.

Default assemblies also pass `tools/validate-external-assembly-references.ps1`,
which rejects unknown hard third-party asmdef references.

Unity CI now copies every `com.zerogamestudio*` package into the temporary test
project and marks every package with a `Tests` folder as testable.

`modsystem` already uses `defineConstraints` for legacy and Steam optional
assemblies. Projects that enable those symbols should run the matching optional
integration test path before release.

### Runtime Project Assumptions

The first grep pass found hard-coded resource/path/scene lookups in multiple
packages. This branch removed the `Resources.Load`, scene find API, and
hard-coded `Assets/` path occurrences covered by
`tools/validate-runtime-source-policy.ps1`.

The policy now runs in CI with an empty known-debt allowlist. New runtime uses
of `Resources.Load`, scene find APIs, or hard-coded `Assets/` paths fail the
static gate. Runtime source also cannot import `UnityEditor` directly, and
fully qualified `UnityEditor.*` calls must stay behind `UNITY_EDITOR` guards.

### Config Workbench Coverage Gate

`tools/validate-config-workbench-coverage.ps1` now scans package Runtime and
Editor source for `CreateAssetMenu` configuration assets. The current pass
covers 63 config types across 17 packages and fails CI if new config assets miss
designer-facing menu metadata or the shared DataToolkit workbench/validation
surface is removed. The same gate now requires every package with config assets
to expose a package-local Editor `*ConfigValidator`, verifies those validators
use the `IEnumerable<TConfig>` signature that DataToolkit can bridge, and
checks that validation issues expose severity, field path, message, and asset
mapping data for designer-facing reports. It also requires each package-local
validator class to be referenced by package-local tests so new config validation
surfaces cannot enter CI without direct test coverage.

## Validation Evidence

The final package tar was validated on `zgs-build` with Unity 2022.3.62f3 from
`H:/CodexTemp/zeroengine-package-audit-20260619-212528`.

- Static gates passed for package boundaries, package metadata, consumer install
  docs, shipping cleanliness, Unity meta pairs/GUID uniqueness, Unity test
  assemblies, asmdef isolation, Unity package dependencies, external assembly
  references, runtime source policy, and config workbench coverage.
- Static coverage counts: 28 packages, 2,197 shipped package files, 800
  package assets with 800 paired `.meta` files, 1,260 package meta GUIDs, 81
  asmdef files, 29 test asmdefs, 74 C# test files, 509 test attributes, 533
  Runtime C# files, 63 `CreateAssetMenu` config types, and 17 package
  `*ConfigValidator` files.
- Unity package import and compile completed with exit code 0.
- EditMode execution produced an NUnit XML result of `Passed`: 510 total, 510
  passed, 0 failed, 0 skipped, and 0 inconclusive.

The first `-quit -runTests` command confirmed import/compile but did not write
test XML in this environment. The final evidence above comes from the no-quit
Unity batchmode test command that produced `editmode-results-noquit.xml`.

## Merge Conclusion

All package-graduation gates required for this batch are green. The branch is
merge-ready to `main` from a package validation standpoint, pending the normal
repository review/commit/push/CI process.

No commit, push, PR, or merge was performed by this audit.

## Ongoing Gates

1. Keep the package boundary audit green on every PR.
2. Keep `package.json` internal dependencies aligned and update touched READMEs
   plus consumer documentation where install requirements change.
3. Classify every hard external asmdef reference as required or optional.
4. Split optional integrations into optional asmdefs or declare them as real UPM
   dependencies.
5. Keep `data-toolkit` as the shared config workbench foundation: discovery,
   generic semantic validation, package-specific validator extension, CSV
   export, CSV field import preview, batch field editing, reference inspection,
   Undo, dirty-save handling, and test coverage should stay green.
6. Broaden optional-symbol and newly promoted config surfaces with matching
   package-local validators and Editor tests when those surfaces become part of
   a release target.
7. Keep package-level Editor tests for config discovery and validation.
8. Keep the config workbench coverage gate green as new `CreateAssetMenu`
   config types are added.
9. Keep Unity `.meta` GUIDs unique and asmdef isolation green as packages are
   split, copied, or renamed.
10. Keep package shipping cleanliness green so local agent plans, editor
    settings, and backup files do not ship through UPM packages.
11. Keep the runtime source policy at zero known debt and expand it when new
   hidden project-assumption patterns are found.
12. Enrich package CHANGELOG histories before public version bumps.
13. Run Unity package import/test smoke checks before merging each package
    graduation batch.
