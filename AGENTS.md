# AGENTS.md

## Project Role

This repository is the ZeroEngine development workspace. Unity projects consume
verified packages from ZE Registry, signed release archives tracked in SCM, or
pinned Unity Package Manager Git dependencies from
`https://github.com/ZeroGameStudio-CN/zeroengine.git`.

Development-content records and their format follow
[the project adapter](docs/agent/project-adapter.md).

Do not tell consumer projects to copy a `ZeroEngine` folder into `Assets` or to
depend on this local checkout as their normal setup.

## Agent Guidance

The [ZE Agent package](com.zerogamestudio.zeroengine.ai-support/README.md)
owns the Skill template. Consumer `.agents/skills/zeroengine` files describe
the packages actually installed by that project.

Each module's README describes its API, dependencies and usage. Update the
README and affected guidance with the module. The [ZE Registry contract](Tools/Packages/registry-preview/README.md)
owns admitted versions and the release workflow.

## Consumer Project Rules

- Pin admitted ZE Registry packages to the tested versions in
  `Packages/manifest.json` and commit the matching UPM lock. For other modules,
  use Git UPM URLs:
  `https://github.com/ZeroGameStudio-CN/zeroengine.git?path=<package>#<tested-commit>`.
- Pin shared Git dependencies to tested commits. Keep the project on a
  verified compatible package cohort when moving its pins or switching sources.
- For a project that distributes signed archives, follow its archive inventory
  and exact relative `file:` pins. Restore the project's verified delivery route
  after temporary local debugging.
- Include required `com.zerogamestudio.*` sibling dependencies from the same
  verified cohort.

## Editing Rules

- Keep changes surgical and tied to the requested task.
- Preserve UPM package boundaries: each top-level `com.zerogamestudio.*`
  directory is its own package with its own `package.json`, `.asmdef` files,
  `README.md`, and `.meta` files.
- Use `ZeroGameStudio` for `author.name` and `ZeroEngine <Module>` for ZE package
  display names. Display metadata is versioned with the release archive.
- When changing package dependencies, update the relevant `package.json`,
  README, and consumer setup documentation together.
- Do not edit generated Unity folders such as `Library`, `Temp`, `Obj`,
  `Build`, `Builds`, or package cache directories.
- Preserve `.meta` files when adding, moving, or deleting Unity assets.

## Search And Verification

- Prefer scoped `git grep`, `rg`, or `git ls-files` searches from the repo root.
- Avoid broad recursive searches outside this repository.
- For docs-only changes, verify with targeted searches for stale install
  patterns and inspect `git diff`.
- For code changes, run the narrowest Unity EditMode, PlayMode, or package test
  that proves the touched behavior before handoff.
