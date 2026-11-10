# ZeroEngine package consolidation

Consolidate the existing package set before implementing the native command
system. The command system and its AI Skill start only after the package set
has passed its consumer checks. No target user release is assigned yet.

G3 engineering acceptance is frozen at `8441097`; the
[acceptance record](../packages/2026-11-10-g3-acceptance.md) defines its evidence
and explicit consumer/platform boundaries. The native Console can now proceed
against that immutable source baseline.

The [package review](../packages/2026-11-10-consolidation-review.md) records all
57 reusable source packages and the three project-owned POB integration packages,
including decisions, incompatible consumer upgrades and verification boundaries.

## Source and compatibility

The initial integration base is the P5-adopted commit
`1b9c28ccf344db7f37b2aab25e964a3fd40c9887`. Compare each package against the
commits actually used by POB and the other known consumers. Preserve independent
changes through package-scoped three-way integration; neither an older main
branch nor a single consumer can silently replace the other consumers' features.

Keep project adapters and embedded experiments in their consumer projects until
their reusable contract and adoption have been established. Temporary consumer
verification must restore its inputs; normal dependencies remain tested Git pins.

## Package ownership decisions

- The standalone ModSystem package owns the neutral manifest and loading
  contracts. The legacy bundle owns the older parser, hot reload, Lua integration
  and legacy editor tools. Neither package ships duplicate copies of those types,
  assembly definitions or GUIDs. Keep the newer compatibility fixes when
  consolidating the copies. Existing manifest fields and asset GUIDs remain valid;
  moving component assemblies requires explicit migration metadata and readback.
- Achievement owns the achievement tests that moved with its runtime. Remove the
  legacy test copy after comparing its cases, retaining the package tests.
- The legacy bundle declares the modular assemblies it actually uses. Combat
  declares its Economy dependency. Runtime diagnostics do not reference unrelated
  networking, localization or editor-facing assemblies merely because those
  packages were once bundled together.
- Use canonical repository metadata and the repository license in every package.
  Do not change author identity, public package IDs or consumer pins to achieve
  metadata consistency.

## Verification and remaining integration

`Tools/Packages/audit_packages.py` checks package metadata, internal dependency
closure, duplicate asset GUIDs, duplicate assembly names and assembly cycles.
It also checks orphan file metadata, direct JSON Serialize usage and production
Test Runner references. `create_test_project.py` selects the compatible sibling
closure for an Editor version instead of importing Unity 6-only code into every
Unity 2022.3 CI lane.
Its report is generated to a caller-selected external path. Static checks do not
establish Unity import, runtime behavior or consumer adoption.

Resolve source differences in the configuration pipeline, stats/buffs, rendering
and the remaining shared modules before choosing the final version combination.
Use relevant package tests and isolated Unity imports, then the corresponding
POB/P5 consumer routes. Missing third-party integrations must have an explicit
installation contract and cannot be treated as supported by a successful import
in a project that happened to contain extra plugins.

Development progress and evidence belong to
`docs/agent/development-content-plan.json`; this specification records decisions
and completion conditions, not a second task queue. Publication and consumer
adoption remain separate from local implementation and verification.
