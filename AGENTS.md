# AGENTS.md

## Project Role

This repository is the ZeroEngine development workspace. Unity projects consume
verified packages from ZE Registry or use pinned Unity Package Manager Git
dependencies that point at `https://github.com/ZeroGameStudio-CN/zeroengine.git`.

Development-content records and their format follow
[the project adapter](docs/agent/project-adapter.md).

Do not tell consumer projects to copy a `ZeroEngine` folder into `Assets` or to
depend on this local checkout as their normal setup.

## Agent Guidance

ZE Agent's maintained source is
[the Agent package](com.zerogamestudio.zeroengine.ai-support/README.md):
`Skills~` owns the project Skill template and `Tools~` its explicit lifecycle CLI.
Consumer `.agents/skills/zeroengine` files are generated from actual installed
packages; maintain the source template and module READMEs here.

The isolated package distribution Preview is maintained in
[the registry tools](Tools/Packages/registry-preview/README.md). It supports
signed releases from committed source, a Worker01 origin and same-address
GitHub fallback. The admission manifest lists the exact available versions.

When a module's API, dependencies or usage changes, update its README and any
affected Agent guidance together. After consumer package installation, removal or
upgrade, refresh the inventory and run the Skill check. Keep this `AGENTS.md`
current when repository architecture or source entrances change.

## Consumer Project Rules

- Pin admitted ZE Registry packages to the tested versions in
  `Packages/manifest.json` and commit the matching UPM lock. For other modules,
  use Git UPM URLs:
  `https://github.com/ZeroGameStudio-CN/zeroengine.git?path=<package>#<tested-commit>`.
- Pin shared Git dependencies to tested commits. Keep the project on a
  verified compatible package cohort when moving its pins or switching sources.
- Use `file:` dependencies only for temporary local debugging. Replace them
  with pinned Git URLs before handoff.
- Include required `com.zerogamestudio.*` sibling dependencies from the same
  verified cohort.

## Editing Rules

- Keep changes surgical and tied to the requested task.
- Preserve UPM package boundaries: each top-level `com.zerogamestudio.*`
  directory is its own package with its own `package.json`, `.asmdef` files,
  `README.md`, and `.meta` files.
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
