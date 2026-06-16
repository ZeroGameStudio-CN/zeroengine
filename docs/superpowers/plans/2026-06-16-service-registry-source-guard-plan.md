# ZeroEngine Service Registry And Source Guard Plan

> **For agentic workers:** Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable service registry and source guard scanner utilities to ZeroEngine packages for P5 consumption.

**Branch:** `codex/p5-wave5-service-registry-source-guard`

---

## Task 1: Core Service Registry

**Files:**

- Create: `com.zerogamestudio.zeroengine.core/Runtime/Core/ServiceRegistry.cs`
- Create: `com.zerogamestudio.zeroengine.core/Runtime/Core/ServiceRegistry.cs.meta`
- Create: `com.zerogamestudio.zeroengine.core/Tests/Editor/Core/ServiceRegistryTests.cs`
- Create: `com.zerogamestudio.zeroengine.core/Tests/Editor/Core/ServiceRegistryTests.cs.meta`

- [x] Add `ZeroEngine.Core.ServiceRegistry` with generic `Register`, `TryResolve`, `ResolveOrNull`, `Unregister`, `OverrideForTests`, and `ClearForTests`.
- [x] Keep implementation free of P5, scene lookup, Addressables, and domain-specific service types.
- [x] Add package tests for the full registry behavior used by P5.

## Task 2: EditorTools Source Guard Scanner

**Files:**

- Create: `com.zerogamestudio.zeroengine.editortools/Editor/SourceGuards/SourceGuardScanner.cs`
- Create: `com.zerogamestudio.zeroengine.editortools/Editor/SourceGuards/SourceGuardScanner.cs.meta`
- Create: `com.zerogamestudio.zeroengine.editortools/Tests/Editor/SourceGuardScannerTests.cs`
- Create: `com.zerogamestudio.zeroengine.editortools/Tests/Editor/SourceGuardScannerTests.cs.meta`

- [x] Add a small scanner that returns relative path, line number, normalized line, and stable key for matching lines.
- [x] Allow callers to pass a relative-path exclusion predicate.
- [x] Add package tests using temporary files.

## Task 3: Verify, Review, Push

- [x] Run focused package tests for `ServiceRegistryTests` and `SourceGuardScannerTests`.
- [x] Run source checks showing no P5 references in the touched ZE packages.
- [x] Write `docs/reviews/2026-06-16-service-registry-source-guard-review.md`.
- [x] Commit and push the branch.
- [x] Record the pushed commit hash for P5 package pinning.
