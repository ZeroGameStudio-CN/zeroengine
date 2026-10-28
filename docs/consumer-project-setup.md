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

### Unity 6 pixel rim lighting

`com.zerogamestudio.zeroengine.render.urp` is a separate Unity 6000.6 / URP 17.6
package. Add its official Git UPM URL pinned to the tested commit and reference
`ZeroEngine.Render.Urp` from the consumer's runtime assembly. It has no dependency
on consumer gameplay or on the UnityEngine-only `zeroengine.render` primitives.

Register `SpriteRimRendererFeature` on each game Renderer. At the consumer's actor
enable/renderer-change entry, configure `SpriteRimLighting` with the body renderers;
disable it on return to the pool. Add `SpriteRimLightSource` only to native Point
and Spot lights intended to affect actor contours. Unmarked illumination,
Directional lights and sky ambience retain their base-material lighting only.
The consumer owns default activation, its global switch and actor discovery.
See the package README for explicit 2D lights, supported material queues and tests.

Add the package to `testables` for its EditMode and Render Graph PlayMode checks.
Verify real actor entry and reuse in the consumer before adopting the tested pin.

### Package resolution

- Confirm the package path matches a top-level package directory in this repo.
- Use the same commit for related ZeroEngine packages.
- If a package cannot be resolved, check for an internal
  `com.zerogamestudio.*` dependency that also needs a manifest entry.
- Do not edit `Library` or cached package contents; change the manifest, then
  let Unity resolve again.

### Body visuals

Install `com.zerogamestudio.zeroengine.render` and
`com.zerogamestudio.zeroengine.render.urp` from the same tested official Git
commit. The core accepts explicit pose, clock and resource inputs. The URP
backend supplies shaders; Buff/Ability lifecycle binding and game asset loading
remain consumer adapters. Body appearance does not require replacing a project's
Buff implementation. Do not ship temporary absolute `file:` dependencies.
