# Atlas module sources and local index (1.2)

Status: implemented locally; release and consumer activation pending. Extends the
existing Project Atlas contract; no new service, scheduler, database or assembly
boundary is introduced. Existing Editor assembly remains the Unity owner; the
.NET CLI compiles the same catalog/model/projection source files without Editor
provider discovery. Project-specific resolvers and coverage remain Editor gates.

## Contract

- Root schema 1 remains explicit sources + tracked index, unchanged.
- Root schema 2 adds sourceDirectories below docs/architecture/project-atlas.
  Discover only *.atlas.json, deterministically sorted; reject links, traversal,
  missing roots, duplicate sources and duplicate reference/system/module IDs.
- A module owns references and append-only contributions to a named system's
  program entry/structure/verification refs and data-flow descriptions. It cannot
  override ownership, lifecycle, team/Agent policy or another module's definition.
  System contract files own those decisions. New modules need no central registration.
- Schema-2 index lives at .zeroengine/project-atlas/system-routing-index.md and
  must be ignored by SCM. It is a projection of authoring sources, not a source.
  Every read reloads and validates all declared sources before deterministic
  rendering; edits/additions/deletions are never hidden behind a cached file.
  No silent stale-cache fallback. Atomic replacement avoids partial output.
- CLI read/check are authoring checks, not a substitute for project reference and
  coverage validation in Editor. Schema-2 rendering is independent of live
  resolver diagnostics so the CLI and Editor produce identical text. Coverage
  diagnostics remain available from the full Editor graph.

## Migration and recovery

Prepare migration outside the consumer via Tools~/prepare_migration.py. It
preserves all references, system contracts, exclusions and program membership;
reference-list display order becomes stable module-ID order. Review its receipt,
then validate both the prepared graph and project coverage before activation.
Consumer activation must update the tested immutable Git package pin, root,
module files, Agent/read entry, tests and SCM ignore policy together. Only after
readers use the CLI should the tracked generated index be removed. Keep unrelated
pending sources; never use blanket remote/local conflict selection.

POB first migration snapshot has 129 references and 4 governing systems, extracted
into independent reference/module declaration files plus system contracts. Existing
legacy narrative stays with its contract; new module-specific routes/descriptions
belong in the module contribution. No automated inference rewrites business meaning.

Rollback restores the old root/sources and pinned package, then regenerates its
schema-1 index. No gameplay, player save, prefab or production configuration migration.

## Verification

Shared C# authoring/model/resolver/coverage tests run in .NET and Unity (UI tests
remain Unity-only). Test new files without root changes, duplicate IDs, invalid
paths, add/edit/delete invalidation, no overwrite of legacy index, legacy schema,
module contributions, and Editor/headless projection equivalence. Consumer
migration must prove graph membership preserved and source hashes unchanged.

## Current validation / handoff

Shared package 1.2 implementation is ready for immutable consumer pins. On
2026-09-26 the POB Unity 2022.3 Editor passed 53 focused tests (50 package and
3 migrated consumer checks), 0 failures/skips. The exact schema-2 candidate
preserved 129 references, 4 system contracts and all program membership; only
reference display ordering changed. Editor and CLI projections were equal.
Offline regression passes 36 C# tests and 6 synthetic migration tests. Compilation
passed with 0 errors/warnings. Schema-1 rendering was identical after accounting
for the temporary local package URI; no compatibility change to legacy projects.

Consumer files were restored after testing. POB activation requires re-reading
current sources, validating migration receipt hashes, updating manifest+lock to
this released commit, enabling module discovery, updating the Agent reader and
coverage check, and ignoring /.zeroengine/project-atlas in Plastic (.gitignore
uses the equivalent directory rule). Remove the four replaced JSON files and
tracked generated index only after the new source/read path is verified.

Concurrent unrelated pending package/Atlas changes must stay preserved and must
not be silently included in the migration commit. The migration tool prepares
external candidates only and never mutates or submits a consumer workspace.
Existing independent ZE package pins in POB remain unchanged for the validated
version split; Atlas API/package dependencies have not changed. Other projects
opt in explicitly and retain schema 1 until migrated.
