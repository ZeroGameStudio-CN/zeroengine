# TCE Release Gates

Run these checks before publishing or pinning ZeroEngine TCE package updates for downstream projects.

## Build Gates

```powershell
dotnet build .\ZeroEngine.TCE.Tests.Editor.csproj --no-restore --nologo
dotnet build .\ZeroEngine.TCE.ModSystem.Tests.Editor.csproj --no-restore --nologo
dotnet build .\ZeroEngine.Tests.Editor.csproj --no-restore --nologo
```

Expected result: all builds finish with zero errors. The base ZeroEngine build may report the known `AbilityDataSO` obsolete warning from historical ModSystem registry code.

## Unity EditMode Gates

Run these Unity Test Runner assemblies and verify `total > 0` and `failed = 0`:

- `ZeroEngine.TCE.Tests.Editor`
- `ZeroEngine.TCE.ModSystem.Tests.Editor`
- `ZeroEngine.Tests.Editor`

The TCE test assembly includes deterministic checks for `component-catalog.md`, `graph.schema.json`, schema versioning, migration, validation, editor source boundaries, samples, and this release gate runbook.

## Static Source Gates

Run boundary searches before checkin:

```powershell
rg -n "ZeroEngine\.Gameplay|ZeroEngine\.Combat|POB|P5" Packages/com.zerogamestudio.zeroengine.tce/Runtime Packages/com.zerogamestudio.zeroengine.tce/Editor -g "*.cs" -g "*.asmdef"
rg -n "ZeroEngine\.Gameplay|ZeroEngine\.Combat|POB|P5" Packages/com.zerogamestudio.zeroengine.tce.modsystem Packages/com.zerogamestudio.zeroengine.modsystem/Runtime -g "*.cs" -g "*.asmdef"
```

Expected result: no matches.

Run placeholder and whitespace scans on the touched release paths:

```powershell
rg -n "TB[D]|TO[D]O|PLACEHOLDE[R]|FIXM[E]|待[定]|占[位]" docs/superpowers/plans Packages/com.zerogamestudio.zeroengine.tce Packages/com.zerogamestudio.zeroengine.tce.modsystem
rg -n "[ \t]+$" docs/superpowers/plans/2026-06-10-ze-tce-release-gates-phase8.md Packages/com.zerogamestudio.zeroengine.tce/Documentation~/release-gates.md Packages/com.zerogamestudio.zeroengine.tce/Tests/Editor/TceReleaseGateTests.cs
```

Expected result: no matches in the touched release paths. If broader scans report historical placeholder markers or generated Unity `.meta` empty fields, narrow the scan to the current release paths and record the exception.

## Package Manifest Gates

Before release, confirm:

- `com.zerogamestudio.zeroengine.tce/package.json` has the intended package version;
- `com.zerogamestudio.zeroengine.tce.modsystem/package.json` pins the same TCE package version;
- bridge dependencies stay limited to standalone ModSystem, TCE, and Newtonsoft JSON;
- downstream projects pin either the released package version or a committed ZeroEngine package hash.

Do not include `Packages/packages-lock.json` in a TCE release checkin unless its diff contains only the intended package lock update.

## Plastic Gate

Review workspace state before submit:

```powershell
cm status --short
```

Submit with explicit paths through `plastic-commit.ps1 -Paths ... -DryRun -ShowAllFiles`, then check in only after the dry-run scope contains the current phase files and required `.meta` files.
