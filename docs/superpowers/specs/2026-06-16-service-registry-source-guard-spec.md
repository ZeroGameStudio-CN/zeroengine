# ZeroEngine Service Registry And Source Guard Spec

- Date: 2026-06-16
- Scope: reusable framework utilities needed by P5 post-graduation Wave 5.
- Source branch base: P5 current ZeroEngine pin `fb815b600577442dc98d04a2c0123f55f73254eb`.

## Problem

P5 currently owns two generic utilities that are not project-specific:

- a small generic service registry in `ZGS.Core.ServiceRegistry`;
- repeated source guard scan mechanics in P5 EditMode tests.

These are reusable framework concerns and should live in ZeroEngine packages. P5 should keep only project registrations, project rule definitions, and compatibility facades.

## Goals

- Add `ZeroEngine.Core.ServiceRegistry` with the same generic register/resolve/unregister/test-override behavior P5 already uses.
- Add a small `ZeroEngine.EditorTools` source guard scanner that handles file enumeration, path normalization, whitespace normalization, and stable line-match keys.
- Keep ZeroEngine free of P5 references, P5 folder rules, P5 allowlists, gameplay types, and project-specific validation rules.
- Provide package tests for both utilities.

## Non-Goals

- Do not rewrite all P5 call sites to direct `ZeroEngine.Core.ServiceRegistry` usage in the first migration.
- Do not add dependency injection containers, service lifetimes, scoped worlds, async service initialization, or Unity scene lookup.
- Do not encode P5 retired pattern, lifecycle, `.Forget()`, or Addressables rules in ZeroEngine.
- Do not edit P5 `Library/PackageCache`.

## Acceptance

- `ZeroEngine.Core` has package tests for the registry contract.
- `ZeroEngine.EditorTools` has package tests for source guard scanner behavior.
- ZeroEngine source does not reference P5 assemblies or P5 domain types.
- The branch is pushed and P5 can pin every `com.zerogamestudio.zeroengine.*` package to the same new commit.
