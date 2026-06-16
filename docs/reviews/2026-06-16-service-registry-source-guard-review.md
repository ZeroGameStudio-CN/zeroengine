# ZeroEngine Service Registry And Source Guard Review

- Date: 2026-06-16
- Branch: `codex/p5-wave5-service-registry-source-guard`
- Package implementation commit: `d9ef665f7754a61c55e79dc3a954deed8bb511a6`
- Status: Package implementation verified through P5 package pin.

## Result

Added reusable ZeroEngine utilities:

- `ZeroEngine.Core.ServiceRegistry`
  - Generic static service registry.
  - Supports register, try-resolve, resolve-or-null, unregister, test override scopes, and test clearing.
  - No Unity scene lookup, Addressables loading, P5 type references, or domain-specific service knowledge.
- `ZeroEngine.EditorTools.SourceGuards.SourceGuardScanner`
  - Enumerates C# files under a caller-provided root.
  - Normalizes relative paths and line whitespace.
  - Returns stable line match keys.
  - Lets projects pass their own relative-path exclusion predicate.

P5 keeps project-specific source guard allowlists and a `ZGS.Core.ServiceRegistry` compatibility facade. The generic implementation now lives in ZE.

## Verification

P5 pinned all `com.zerogamestudio.zeroengine.*` packages to implementation commit `d9ef665f7754a61c55e79dc3a954deed8bb511a6` and resolved PackageCache before running tests.

Package cache verifier:

- `tools/Verify-ZeroEnginePackageCache.ps1 -WaitSeconds 0`
- `ZeroEnginePackageCount=21`
- `UniquePinCount=1`
- `CurrentPin=d9ef665f7754a61c55e79dc3a954deed8bb511a6`
- `PackageCacheIssueCount=0`
- `Result=Pass`

Focused EditMode job:

- Job: `df43f43551bc45d6a9ad8de8284bdb2b`
- Scope:
  - `ZeroEngine.Core.Tests.ServiceRegistryTests`
  - `ZeroEngine.EditorTools.Tests.SourceGuardScannerTests`
  - P5 service registry facade and source guard adapter tests
  - P5 ZeroEngine package governance tests
- Result: `38/38`, failed `0`

Fast P5 EditMode:

- Job: `1407145eae0e4dfba02e6b003735b296`
- Scope: `ZGS.Tests.EditMode`, categories `Unit;Boundary`
- Result: `1111/1111`, failed `0`

Console filters after verification:

- `error CS`: `0`
- `Compilation failed`: `0`

## Package Resolve Note

After the first P5 pin update, Unity still served old `Library/PackageCache` entries, so `ZeroEngine.Core.ServiceRegistry` was not visible to compilation. The root cause was stale generated PackageCache content, not a source API issue. The fix was to remove only generated `Library/PackageCache/com.zerogamestudio.zeroengine.*@*` entries and trigger Unity `resolve_packages`; PackageCache then restored the new package payloads.

## Scope

ZE touched:

- `com.zerogamestudio.zeroengine.core/Runtime/Core/ServiceRegistry.cs`
- `com.zerogamestudio.zeroengine.core/Runtime/Core/ServiceRegistry.cs.meta`
- `com.zerogamestudio.zeroengine.core/Tests/Editor/Core.meta`
- `com.zerogamestudio.zeroengine.core/Tests/Editor/Core/ServiceRegistryTests.cs`
- `com.zerogamestudio.zeroengine.core/Tests/Editor/Core/ServiceRegistryTests.cs.meta`
- `com.zerogamestudio.zeroengine.editortools/Editor/SourceGuards.meta`
- `com.zerogamestudio.zeroengine.editortools/Editor/SourceGuards/SourceGuardScanner.cs`
- `com.zerogamestudio.zeroengine.editortools/Editor/SourceGuards/SourceGuardScanner.cs.meta`
- `com.zerogamestudio.zeroengine.editortools/Tests/Editor/SourceGuardScannerTests.cs`
- `com.zerogamestudio.zeroengine.editortools/Tests/Editor/SourceGuardScannerTests.cs.meta`
- `docs/superpowers/specs/2026-06-16-service-registry-source-guard-spec.md`
- `docs/superpowers/plans/2026-06-16-service-registry-source-guard-plan.md`
- `docs/reviews/2026-06-16-service-registry-source-guard-review.md`

No P5 source, assets, scenes, prefabs, package pins, or generated cache files are stored in the ZE repo.
