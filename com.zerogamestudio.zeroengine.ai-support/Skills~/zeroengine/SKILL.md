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

- Follow the project manifest's package source. Pin admitted ZE Registry
  packages to verified versions. For Git UPM packages, pin the tested commit.
- Upgrade the affected modules and required siblings as one compatible cohort.
- Submit the matching manifest, lock and refreshed Skill together. For signed
  archive delivery, include `Packages/ZEArchives/archive-lock.json` and the
  active archives. Remove replaced versions from that directory before syncing.
- Before removal, check remaining package and project assembly references.
- Verify actual PackageInfo, manifest/lock and relevant compilation, then refresh
  guidance. Installed source alone does not prove Player support or release.

## Publish a ZE version

From the ZE repository, use `Tools/Packages/registry-preview/release.py`
with the tested commit and changed package IDs. The command signs, verifies
and publishes the artifacts and writes the verified admission catalog. It checks
package authors, display names and the signed archive set. Use `--check` to
preview the checks, with `--project` for a signed archive consumer.
Use the [Registry release guide](https://github.com/ZeroGameStudio-CN/zeroengine/blob/main/Tools/Packages/registry-preview/README.md#publish-a-version) for the command and recovery receipts.

## Skill lifecycle

Export `ZeroEngine.AiSupport.Editor.SkillPackageSnapshot.CreateJson()` through the
existing Editor route to an external JSON file. Run the installed Agent package's
`Tools~/sync_skill.py --project <project> --snapshot <snapshot>` to preview;
add `--apply` to synchronize or `--check` to detect drift.
Both modes check the managed archive inventory, files and hashes when present.

Local/embedded sources and split trials use the README's explicit flags. Preserve
custom files, modified managed files, unknown receipts and newer tool versions.
An older copied script points to the installed tool instead of updating itself.

Before uninstalling Agent, run its installed tool with `--project <project>
--remove`, then `--apply`. This removes unchanged generated guidance only; custom
files remain. Do not run removal from the copy being removed. UPM removes packages.

Maintain templates/tools in ZE's Agent package and module APIs in their READMEs;
consumer Skill files are generated. Follow project authority for builds, publishing
and external messages; this Skill grants none.
