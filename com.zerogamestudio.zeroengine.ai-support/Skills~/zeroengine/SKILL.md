---
name: zeroengine
description: Use installed ZeroEngine modules and manage their UPM installation, upgrades, removal and version-aware project guidance, including the native Console.
---

# ZE Agent in this project

Use the project's actual installed modules and existing engineering route.
Start with [the generated package inventory](references/installed-packages.md),
then read only the relevant module README and source entrance from
`references/installed-packages.json`. An absent module is not an available API.
Versions and Git commits belong to each module; a package-cache folder suffix
is not a tested revision.

Use Git UPM dependencies pinned to a tested commit. Include required sibling
packages explicitly at the same commit. A temporary local dependency or an
authorized version split remains a trial; preserve its provenance and other
owners' pins. Do not copy package folders into `Assets` as normal installation.

For command registration, automation, completion or a runtime debug console,
read [the native Console contract](references/console.md) and the installed
Console module READMEs. Keep gameplay modules independent of a Console adapter.
Other modules retain their own namespaces, lifetimes and dependencies; resolve
them from their installed source rather than assuming the legacy bundle's API.

## Install, upgrade or remove modules

Select modules from their own README and declared dependencies. Use the consumer's
existing Unity Package Manager route: `UnityEditor.PackageManager.Client` through
its authorized Editor/CLI executor, or the Package Manager UI. The Unity CLI is an
Editor executor, not a separate package resolver. Do not launch a competing Editor.

Add required sibling packages explicitly with the same tested Git commit. For an
upgrade, update the installed ZE cohort together unless this is an explicit split
trial. Before removal, check remaining package and project assembly references;
do not remove shared dependencies still in use. Preserve unrelated pending work.

Observe the original asynchronous UPM request, then verify registered PackageInfo,
the manifest/lock pair and relevant compilation. Export a new snapshot and refresh
this Skill so the inventory follows installed source, including removed modules.

This guidance establishes source availability, not successful compilation,
Player support, consumer acceptance or release. Select verification from the
changed behavior and the project's rules. Package documentation and this Skill
do not authorize builds, publishing, credential changes or external messages.

## Refresh guidance

Export `ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` through
the project's existing Editor/CLI route, save its returned JSON externally,
then run the installed ZE Agent package's `Tools~/sync_skill.py` with
`--project` and `--snapshot`. Preview first; `--apply` performs the explicit
project-only synchronization. The copied `scripts/sync_skill.py` also works
when it still matches the installed source. An older copy identifies the current
installed entry rather than upgrading itself.

Keep local edits, unknown receipt schemas and newer tool versions. A mixed-pin
or local-source inventory needs its explicit investigation flag and keeps
per-module versions. Synchronization never repins packages, installs a global
Skill or starts a watcher. See the installed ZE Agent README for conflict and
recovery semantics.

Before uninstalling ZE Agent itself, run its installed `Tools~/sync_skill.py`
with `--project <project> --remove` to preview guidance removal, then add `--apply`.
This removes only unchanged managed files and preserves local additions; it does
not uninstall UPM packages. Use the installed package tool, not the running copy
inside the Skill being removed. Source template and shared lifecycle maintenance
belong in ZE's Agent package; project-specific additions remain in the consumer.
