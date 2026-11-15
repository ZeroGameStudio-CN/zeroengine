---
name: zeroengine
description: Use installed ZeroEngine modules and manage their UPM installation, upgrades, removal and project guidance, including ZE Console.
---

# ZE Agent

Start with [the installed inventory](references/installed-packages.md), then read
the relevant module README and source path from
`references/installed-packages.json`. Uninstalled modules are not available APIs.
For commands or a debug console, read [the Console contract](references/console.md).
Keep gameplay independent of a Console adapter.

## Packages

Use the project's existing UPM route: Package Manager UI or
`UnityEditor.PackageManager.Client` through its authorized Editor executor.
Observe the original asynchronous request; do not launch a competing Editor.

- Install needed modules and required siblings with pinned Git UPM URLs at one
  tested commit. Never copy package folders into `Assets`.
- Upgrade the installed ZE cohort together; version splits remain explicit trials.
- Before removal, check remaining package and project assembly references.
- Verify actual PackageInfo, manifest/lock and relevant compilation, then refresh
  guidance. Installed source alone does not prove Player support or release.

## Skill lifecycle

Export `ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` through the
existing Editor route to an external JSON file. Run the installed Agent package's
`Tools~/sync_skill.py --project <project> --snapshot <snapshot>` to preview;
add `--apply` to synchronize or `--check` to detect drift.

Local/embedded sources and split trials use the README's explicit flags. Preserve
custom files, modified managed files, unknown receipts and newer tool versions.
An older copied script points to the installed tool instead of updating itself.

Before uninstalling Agent, run its installed tool with `--project <project>
--remove`, then `--apply`. This removes unchanged generated guidance only; custom
files remain. Do not run removal from the copy being removed. UPM removes packages.

Maintain templates/tools in ZE's Agent package and module APIs in their READMEs;
consumer Skill files are generated. Follow project authority for builds, publishing
and external messages; this Skill grants none.
