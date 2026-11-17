# ZeroEngine TCE Presentation Release Gates

Run these checks before publishing or pinning `com.zerogamestudio.zeroengine.tce.presentation` for downstream projects.

## Unity EditMode Gates

Run these Unity Test Runner assemblies:

- `ZeroEngine.TCE.Presentation.Tests.Editor`
- `ZeroEngine.TCE.Tests.Editor`

Expected result for each assembly: `total > 0` and `failed = 0`.

The presentation test assembly must cover:

- default-compatible playback cleanup;
- alpha fade delay, fade duration, and custom alpha curve playback;
- sprite material and sorting overrides;
- sprite and mesh material override plus configurable tint/main-texture shader properties;
- readable/unreadable mesh snapshot boundaries.
- mixed renderer graphs, shared unreadable mesh resources, source-only callback exclusion, and rejection after a qualified resource changes.

## Static Source Gates

Run presentation runtime boundary checks:

```powershell
rg -n "POB|DG\.Tweening|DOTween|Sirenix|Spine\.Unity|DamageInfo|Projectile|Weapon" Packages/com.zerogamestudio.zeroengine.tce.presentation/Runtime -g "*.cs" -g "*.asmdef"
```

Expected result: no matches.

Run unfinished marker checks:

```powershell
rg -n "T[B]D|TO[D]O|FIX[M]E|place[a-z]*holder|待[定]|未[定]" Packages/com.zerogamestudio.zeroengine.tce.presentation
```

Expected result: no matches.

## Package Manager Gate

Confirm Unity resolves the tested package and sees its expected version. Read
`PackageInfo.FindForAssembly(typeof(TceVisualPrefab).Assembly)` from the consumer's
Editor and retain the package ID, source and resolved path:

```text
PackageInfo.FindForAssembly(typeof(TceVisualPrefab).Assembly)
```

Expected result:

- source: `Git` at the tested pinned commit for handoff (`Local` is a temporary trial only)
- version: `0.4.0`
- dependency_count: `2`
- dependency: `com.zerogamestudio.zeroengine.tce`
- dependency: `com.unity.timeline` at `6.6.0`

## Plastic Gate

Review workspace state before handoff or submit:

```powershell
cm status --short
```

Do not submit unrelated workspace changes. Use explicit paths for any future Plastic dry-run or checkin.

## Release Evidence

Record release evidence with:

- Unity version;
- package version;
- Package Manager source and dependency count;
- Unity test assembly names and pass totals;
- static scan commands and no-match results;
- Plastic status scope and any unrelated pending files.

For authored graph integration run `TceVisualDynamicsTests`: event/controller/audio/physics exclusion, safe Timeline bindings, continuous local sampling and malformed payload rejection. Technology adapters must retain source ownership and prove current source/display resource layout equality. Run the package tests against their actual Package Manager resolved path, including Git and temporary local sources; an unselected test assembly is not a passing gate.
