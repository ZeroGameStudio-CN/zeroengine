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
    "com.zerogamestudio.zeroengine.ui": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.ui#<tested-commit>"
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

Editor-facing packages do not transitively resolve sibling Git packages in
Unity 2022.3. Dashboard 3.1 and Formula 0.5 consumers must therefore declare
`com.zerogamestudio.zeroengine.editor-ui` 1.1 directly at the same tested
commit. Legacy ZeroEngine 2.1 consumers must also declare Dashboard 3.1 at that
commit; never mix these package versions across different Git revisions.

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

`com.zerogamestudio.zeroengine.qte` provides a standalone, engine-independent
`ZeroEngine.Qte` runtime. It has no sibling-package, UI, input-system or save
dependency. Games resolve difficulty and randomness, supply input and gameplay
delta, and bind an immutable snapshot to their own HUD. Add its tested Git pin
only when consuming that runtime; do not install the Gameplay or UI package
merely to use a challenge session. Its package README lists the focused tests.

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


## Fishing core

`com.zerogamestudio.zeroengine.fishing` exposes `ZeroEngine.Fishing` without engine-object, sibling-package, input, UI or save dependencies. Hosts retain species/reward selection and presentation. Supply `System.Numerics` positions, corrected mouth contacts and one gameplay delta. Use `FishPerception` and snapshot-based `SchoolSteering` for local interest/avoidance, `FishMotion` for movement, and `BaitEncounter` for landed-bait inspection/nibble gates. `FishFight` is a continuous resistance model; choose one catch/loss authority when combining it with a host QTE.

Validate the standalone core and the real consumer adapter before publication under explicit source authority. Consume the tested Git commit using the standard UPM route above. Temporary local references are verification-only and must be replaced by the published commit before the consumer handoff. POB keeps QTE as the sole outcome authority and uses continuous resistance for presentation; it does not grant an additional catch or loss from `FishFight`.
