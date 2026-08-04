# Consumer Project Setup

This guide is for Unity projects that consume ZeroEngine packages.

## Standard Source

Use Unity Package Manager Git dependencies that point at this repository:

```text
https://github.com/ZeroGameStudio-CN/zeroengine.git?path=<package-directory>#<tested-commit>
```

The local `zeroengine-git` checkout is the development workspace for
ZeroEngine itself. Consumer projects should not copy a `ZeroEngine` folder into
`Assets` or depend on an uncommitted local checkout for normal work.

## Manifest Example

Add each required ZeroEngine package to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.core#<tested-commit>",
    "com.zerogamestudio.zeroengine.data": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.data#<tested-commit>",
    "com.zerogamestudio.zeroengine.ui": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.ui#<tested-commit>",
    "com.zerogamestudio.analytics": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.analytics#<tested-commit>",
    "com.zerogamestudio.zeroengine.feedback": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.feedback#<tested-commit>"
  }
}
```

Use a full commit hash that has been built or tested for the consumer project.
Avoid `#main` in production branches because it makes dependency changes
implicit and hard to reproduce.

Keep all ZeroEngine packages in one consumer project on the same commit unless
you are deliberately testing a split. If Unity reports a missing
`com.zerogamestudio.*` dependency, add that package to the manifest with the
same commit.

## Upgrade Flow

1. Make the ZeroEngine change in this repository.
2. Run the relevant package tests or manual Unity validation.
3. Commit and push ZeroEngine.
4. Copy the tested commit hash into the consumer project's
   `Packages/manifest.json` for every ZeroEngine package it uses.
5. Open the consumer Unity project and let Package Manager resolve packages.
6. Run the consumer project's relevant EditMode, PlayMode, or smoke tests.
7. Commit the consumer project's `Packages/manifest.json` and
   `Packages/packages-lock.json` if Unity updated the lock file.

## Temporary Local Development

Use `file:` dependencies only for short local debugging when you need to edit
ZeroEngine and a consumer project together before a ZeroEngine commit exists:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.core": "file:../../zeroengine-git/com.zerogamestudio.zeroengine.core"
  }
}
```

Adjust the relative path to your local checkout. Do not commit `file:`
ZeroEngine dependencies to a shared consumer-project branch unless that branch
is explicitly local-only. Before handoff, replace them with Git URLs pinned to
a pushed ZeroEngine commit.

## Troubleshooting

- Confirm the package path matches a top-level package directory in this repo.
- Use the same commit for related ZeroEngine packages.
- If a package cannot be resolved, check for an internal
  `com.zerogamestudio.*` dependency that also needs a manifest entry.
- Do not edit `Library` or cached package contents; change the manifest, then
  let Unity resolve again.
