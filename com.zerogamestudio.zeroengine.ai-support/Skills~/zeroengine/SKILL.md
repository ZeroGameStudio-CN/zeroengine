---
name: zeroengine
description: Use installed ZeroEngine modules and manage their UPM installation, upgrades, removal and project guidance, including ZE Console.
---

# ZE Agent

Start with the project's guidance and [the installed inventory](references/installed-packages.md).
Read the module README for the API. Look up package source by ID in Unity's
Packages view, or in `Packages/` and `Library/PackageCache/` after package
resolution. The checked-in guidance is readable on a fresh checkout.
Default to ZE Console for new Unity projects. For existing projects, follow
their documented entry and commands. See [the Console contract](references/console.md).

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

The ZE repository owns signing, verification and publication from tested
commits. Use its [Registry release guide](https://github.com/ZeroGameStudio-CN/zeroengine/blob/main/Tools/Packages/registry-preview/README.md#publish-a-version)
for the release entry, metadata checks and recovery receipts.

## Skill lifecycle

For package maintenance, use the [Agent README](references/packages/com.zerogamestudio.zeroengine.ai-support.md)
to export the current Unity package inventory and preview, refresh, check or
remove generated guidance. The installed Agent package owns those operations.
Preserve local additions, modified managed files and unknown receipts.

Maintain templates/tools in ZE's Agent package and module APIs in their READMEs;
consumer Skill files are generated. Follow project authority for builds, publishing
and external messages; this Skill grants none.
