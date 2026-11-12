# ZeroEngine Development Content

Development-content and version planning shared by games, applications and tools.
Development progress, target version and actual user availability remain independent.
The package never enables content, publishes a release, creates execution tasks or
automatically registers assets.

## Install

Use the official Git UPM URL, pinned to a tested commit:

```text
https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.development-content#<tested-commit>
```

Also pin `com.zerogamestudio.zeroengine.editor-ui` to the same commit. The package
requires Editor UI 1.5.0 and `com.unity.nuget.newtonsoft-json` 3.2.1 or later.
Add this package to the consumer manifest's `testables` list to run its tests.

## Ownership and integration

- `ZeroEngine.DevelopmentContent` is a .NET Standard-compatible assembly with no
  Unity references. It owns records, release references, inventory merging,
  validation and guarded JSON storage. Non-Unity adapters can use these sources
  with Newtonsoft.Json without the Editor assembly.
- `ZeroEngine.DevelopmentContent.Editor` supplies an IMGUI workspace panel using
  `ZeroEngine.EditorUI`. It does not depend on Dashboard or any consumer project.
- A project supplies a stable project ID, absolute project root and
  project-relative registry path. Keep records, commands and release evidence
  in that project. An optional inventory adds discoverable trial entrances.

Embed the panel in an existing workspace provider:

```csharp
return new DevelopmentContentPlanPanel(new DevelopmentContentPanelOptions(
    "sample-app", projectRoot, "planning/development-content.json"));
```

For a project with a separate entry catalog, pass an `IDevelopmentContentInventory`
with `Refresh()`, `Entries` and `Error`. Inventory keys are stable bindings and
links are project-relative. Opening the panel reads the registry and optional
inventory. An optional session prefix preserves a migrating adapter's UI state.
By default sessions are isolated by project ID and root.

## Data contract

Schema version 1 stores `projectId`, `currentReleaseId`, `releases` and `records`.
Each record has a stable ID, title, optional inventory binding, stage, target
release, availability, responsible chat, next action, entrance/resource paths,
source revision and release evidence. An empty target means unplanned; `later`
means deferred without a selected release. Changing the current release never
rewrites old targets or marks content released. Internal tools remain outside
the user-release backlog; released and abandoned history stays searchable.

`Previewable` and `DevelopmentOnly` are generic API names. Existing JSON tokens
`Playable` and `EditorOnly` remain the serialized values; both spellings are
accepted on input. Schema 1 preserves project-defined JSON fields on the plan,
each version and each content record. Editing a record through the panel retains
the stored record's extension fields. Shared fields still use the same validation
rules. Future schema versions are refused and released records require actual
release evidence.

Always load before modifying. `DevelopmentContentPlanStore.Save` validates,
checks the loaded byte revision under a path-scoped process mutex and atomically
replaces the full file. Stale saves fail while retaining the caller's draft.
The panel binds session drafts to the registry revision. After a conflicting
update, copy the draft and merge it into the latest record before saving. The
local JSON store is for projects that own their registry file.

## Verification

Package-local EditMode tests cover stable identity, concurrent saves, invalid
schemas, release evidence, version rollover, inventory/orphans, legacy wire
values, non-game projects and read-only panel activation. All writable fixtures
live in OS temporary storage; no consumer registry is edited by tests.
