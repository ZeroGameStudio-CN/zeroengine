# AGENTS.md

## Project Role

This repository is the ZeroEngine development workspace. Other Unity projects
consume ZeroEngine through Unity Package Manager Git dependencies that point at
`https://github.com/ZeroGameStudio-CN/zeroengine.git`.

Do not tell consumer projects to copy a `ZeroEngine` folder into `Assets` or to
depend on this local checkout as their normal setup.

## Consumer Project Rules

- Use Git UPM URLs in `Packages/manifest.json`:
  `https://github.com/ZeroGameStudio-CN/zeroengine.git?path=<package>#<tested-commit>`.
- Pin production and shared branches to a tested commit, not `#main`.
- Keep all ZeroEngine packages in the same consumer project on the same commit
  unless the task is explicitly testing a version split.
- Use `file:` dependencies only for temporary local debugging. Replace them
  with pinned Git URLs before handoff.
- If a consumed package depends on another `com.zerogamestudio.*` package, make
  sure the consumer manifest includes that dependency with the same commit.

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
