---
name: zeroengine
description: Integrate and maintain installed ZeroEngine UPM modules in a Unity project, including the native Console and version-aware project guidance.
---

# ZeroEngine in this project

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

This guidance establishes source availability, not successful compilation,
Player support, consumer acceptance or release. Select verification from the
changed behavior and the project's rules. Package documentation and this Skill
do not authorize builds, publishing, credential changes or external messages.

## Refresh guidance

Export `ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` through
the project's existing Editor/CLI route, save its returned JSON externally,
then run the installed AI Support package's `Tools~/sync_skill.py` with
`--project` and `--snapshot`. Preview first; `--apply` performs the explicit
project-only synchronization. The copied `scripts/sync_skill.py` also works
when it still matches the installed source. An older copy identifies the current
installed entry rather than upgrading itself.

Keep local edits, unknown receipt schemas and newer tool versions. A mixed-pin
or local-source inventory needs its explicit investigation flag and keeps
per-module versions. Synchronization never repins packages, installs a global
Skill or starts a watcher. See the installed AI Support README for conflict and
recovery semantics.
