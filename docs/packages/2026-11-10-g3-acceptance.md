# G3 package baseline — 2026-11-10

Accepted source baseline: `844109708c13e21aeca86a61afa6682cac7fd2de`.
This freezes the package consolidation interfaces for the Console stage. It is
an internal engineering acceptance, independent of production adoption or release.

## Scope and evidence

The [package review](2026-11-10-consolidation-review.md) records a decision for all
57 reusable source packages and the three POB-owned composition packages. The
common candidate retains the consumer-adopted APIs, original asset identities,
standalone neutral ModSystem ownership and the separate legacy compatibility
assembly. Core and Data candidates are 2.2.2; the full version set is in that review.

[Unity CI run 42827294116](https://github.com/ZeroGameStudio-CN/zeroengine/actions/runs/42827294116)
at the accepted commit has successful package-quality checks and all six Unity
2022.3.62f1 combinations: legacy, modular, modules without Dashboard, Dashboard
only, Dashboard with modules, and Core/Audio PlayMode. The audit covers all 57
source packages and the tooling suite has 17 cases. Unity 6-only source and
rendering have the corresponding 6000.6.4f1 evidence recorded in the review.

The single owned native fixture resolves 22 packages from the accepted Git
commit. Frozen actual compiler inputs cover 51 POB and 53 P5 direct production
consumer assemblies. Scoped native flows execute actual POB Stat code and P5's
CharacterStats/memory-save and pool-registration code. The pinned Activity
allocation case succeeds with validated empty and real-allocation controls.
Four inspected real camera readbacks from the published rim fixture confirm
directional lighting and restoration. These checks freeze the affected contracts;
they do not represent complete game-startup or Player certification.

Original failed operations retain their failures. All task-owned native requests
have verified cleanup, including the original hung Core/legacy request after the
user approved ending that exact disposable process. Other unused temporary
Editors were closed. Production POB/P5 manifests, locks, scenes and assets were
not changed. The accidentally produced disposable-fixture default save remains
preserved with its backup; its hash is unchanged after the corrected checks.

## Explicit boundaries

- P6's AutoBattle 1.x, Character 2.x and Data Toolkit 1.x migrations remain separate
  consumer work. Its current pins are preserved. These migrations do not change
  the Console kernel contract and do not block its independent implementation.
- Commercial SDK runtime, full game adoption and Player/platform builds require
  their matching consumer checks. The frozen SDK-surface builds establish only
  C# API compatibility.
- Existing event/settings compatibility APIs remain available. Typed events,
  transactional settings and the legacy gameplay Command pattern keep their
  own contracts. The new Console registry will be a separate, optional facility.
- Quantum Console and existing project command handlers remain in place until
  the later migration and experience gates establish their replacement.

No unresolved package upgrade or ownership change is expected to alter the
Console dependencies. Subsequent package fixes remain incremental, scoped and
verified; they do not silently rewrite this immutable baseline.
