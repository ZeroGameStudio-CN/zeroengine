# Package consolidation review — 2026-11-10

This is an integration candidate, not a published release or a consumer upgrade.
The source set contains 57 reusable package directories. Three POB integration
packages (`com.zerogamestudio.pob.formula`, `.pob.tce`, `.pob.world-shift`) remain
in POB and were reviewed as project-owned composition, bringing the audit scope
to 60 distinct package IDs. Their project types and assets do not move into ZE.

The integration starts from the P5-adopted commit
`1b9c28ccf344db7f37b2aab25e964a3fd40c9887`, then reconciles the package commits
actually used by POB, P6, P8 and P9. Older main is not a substitute for those
consumer branches. P8/P9 Multiplayer's pin is an ancestor of the POB source;
its older config location must not be reintroduced beside the current config.
Incoming tool menus use the existing optional workbench/provider contract; the
original public window/Inspector methods remain available without Dashboard.

## Ownership decisions

The table records the disposition of every reusable source package. Manifest
versions identify this candidate's contents; publication and adoption require
separate evidence. Optional SDK presence is not certified by an import performed
without that SDK.

| Package | Candidate | Minimum Unity | Disposition |
| --- | --- | --- | --- |
| `com.zerogamestudio.analytics` | 2.3.2 | 2021.3 | Retain telemetry, offline queue and feedback delivery; declare its built-in JSON, image and request modules. |
| `com.zerogamestudio.zeroengine` | 3.0.0 | 2022.3 | Compatibility bundle only; neutral ModSystem contracts and achievement tests have a single modular owner. |
| `com.zerogamestudio.zeroengine.achievement` | 1.0.0 | 2022.3 | Own achievement definitions, progress snapshots and platform-provider contracts; remove legacy test duplication. |
| `com.zerogamestudio.zeroengine.activity` | 1.1.0 | 2022.3 | Keep caller-owned activity levels and work budgets; Unity world adapters stay separate from its kernel. |
| `com.zerogamestudio.zeroengine.ai` | 2.0.1 | 2022.3 | Keep FSM, behavior-tree, UtilityAI and schedule APIs; GOAP remains an SDK-gated integration. |
| `com.zerogamestudio.zeroengine.asset-catalog` | 0.1.1 | 2022.3 | Keep generic catalog identity, API and proposal contracts; preserve credential targets and encoding. |
| `com.zerogamestudio.zeroengine.audio` | 2.2.1 | 2022.3 | Keep event IDs, audio-bank, pooling and spatial contracts; declare the Audio module. |
| `com.zerogamestudio.zeroengine.autobattle` | 2.0.0 | 2022.3 | Keep the 2.x deterministic grid/planner kernel. P6 uses the incompatible 1.x simulation API; no automatic pin upgrade. |
| `com.zerogamestudio.zeroengine.capture` | 0.1.1 | 2022.3 | Keep the independent Editor camera-capture tool; projects supply camera and scenario selection. |
| `com.zerogamestudio.zeroengine.character` | 3.0.0 | 2022.3 | Keep P5-adopted 3.x progression and equipment contracts; P6 2.6 requires migration before adoption. |
| `com.zerogamestudio.zeroengine.cinematic` | 2.1.0 | 2022.3 | Keep Timeline playback and binding contracts; declare the Director module. |
| `com.zerogamestudio.zeroengine.combat` | 2.1.0 | 2022.3 | Restore P6 shared effect-parameter contracts and declare Economy plus directly used engine modules. |
| `com.zerogamestudio.zeroengine.config-pipeline` | 2.4.0 | 2022.3 | Merge composite keys and deterministic output with grouped authoring, inline lists and source-preserving candidates. |
| `com.zerogamestudio.zeroengine.core` | 2.2.2 | 2022.3 | Fix persistent-singleton shutdown/ownership cleanup, including PoolManager; declare UIUtils' actual uGUI/UI dependencies and retain legacy EventManager API. |
| `com.zerogamestudio.zeroengine.dashboard` | 4.7.1 | 2022.3 | Keep optional descriptor-driven navigation; it is not a required runtime or module dependency. |
| `com.zerogamestudio.zeroengine.data` | 2.2.2 | 2022.3 | Keep StatId/StatBlock, persistence, bounded sort-layout and repeated-input contracts; merge unobserved Buff batching while preserving observer callbacks. |
| `com.zerogamestudio.zeroengine.data-toolkit` | 2.3.0 | 2022.3 | Keep asset browsing and inspection; restore P6 deterministic validation-report contracts. |
| `com.zerogamestudio.zeroengine.development-content` | 1.1.0 | 2022.3 | Own content/release planning and guarded storage; project registries remain project-owned. |
| `com.zerogamestudio.zeroengine.dlc` | 2.0.0 | 2022.3 | Keep neutral entitlement/content availability contracts; distinct from mod discovery/import. |
| `com.zerogamestudio.zeroengine.dungeon` | 1.0.0 | 2022.3 | Keep reusable dungeon-map, exploration and reward contracts; no forced merge with tactical combat. |
| `com.zerogamestudio.zeroengine.economy` | 2.2.0 | 2022.3 | Merge P6 wallet injection, stock and purchase rollback fixes while retaining the existing transaction systems. |
| `com.zerogamestudio.zeroengine.editor-ui` | 1.5.0 | 2022.3 | Own shared Editor styles and widgets; keep independent of Dashboard and gameplay. |
| `com.zerogamestudio.zeroengine.editortools` | 1.1.1 | 2022.3 | Keep project profiles, validators and generators; explicitly require Test Framework for public test-runner tasks. |
| `com.zerogamestudio.zeroengine.events` | 2.0.0 | 2022.3 | Keep scoped typed buses, queues and Unity channels; legacy string EventManager is not silently replaced. |
| `com.zerogamestudio.zeroengine.extraction` | 0.1.1 | 2022.3 | Keep existing pure extraction rules and public POB.Extraction namespace; project adoption remains explicit. |
| `com.zerogamestudio.zeroengine.feedback` | 1.2.1 | 2022.3 | Keep queue-first player feedback and presentation; transport/storage contracts remain with Analytics. |
| `com.zerogamestudio.zeroengine.fishing` | 0.1.0 | 2022.3 | Keep the caller-clocked fish, bait and resistance kernel; no scene/save/input ownership. |
| `com.zerogamestudio.zeroengine.fogofwar` | 0.2.6 | 6000.6 | Keep registration, CPU sight/exploration and Unity presentation; declare Tilemap and JSON modules. |
| `com.zerogamestudio.zeroengine.fogofwar.urp` | 0.2.5 | 6000.6 | Keep the optional GPU mask backend separate from logical visibility; Unity 6.6 / URP 17.6 contract. |
| `com.zerogamestudio.zeroengine.formula` | 0.6.1 | 2022.3 | Keep deterministic step evaluation, injected randomness and authoring workbench. |
| `com.zerogamestudio.zeroengine.gamefeel` | 0.3.0 | 2022.3 | Keep sensory coordination policies; devices and game-side effect execution stay outside the kernel. |
| `com.zerogamestudio.zeroengine.gameplay` | 2.1.0 | 2022.3 | Preserve merged tutorial, interaction and command-pattern contracts; these are not the future Console command registry. |
| `com.zerogamestudio.zeroengine.haptics` | 1.0.0 | 2022.3 | Keep platform-neutral pulse resolution/arbitration, separate from device-specific vibration APIs. |
| `com.zerogamestudio.zeroengine.input` | 2.1.1 | 2022.3 | Keep device/context/rebinding wrappers; Unity Input System remains an explicit dependency. |
| `com.zerogamestudio.zeroengine.localization` | 2.1.0 | 2022.3 | Keep async localization/fallback/font contracts and the Unity provider; verify SDK presence separately. |
| `com.zerogamestudio.zeroengine.modsystem` | 0.4.0 | 2022.3 | Own neutral manifest, discovery, source and import orchestration; remove copied legacy runtime/editor folders. |
| `com.zerogamestudio.zeroengine.multiplayer` | 0.3.1 | 2022.3 | Keep SDK-neutral sessions plus Local, FishNet and Steam adapters; retain config-assembly migration metadata. |
| `com.zerogamestudio.zeroengine.narrative` | 3.1.0 | 2022.3 | Keep dialogue/quest contracts and legacy achievement adapters; Achievement owns the achievement implementation. |
| `com.zerogamestudio.zeroengine.network` | 2.0.1 | 2022.3 | Retain the NGO/UGS-specific synchronization backend with explicit SDK dependencies; distinct from neutral Multiplayer. |
| `com.zerogamestudio.zeroengine.particle-catalog` | 0.1.1 | 2022.3 | Retain the adopted particle schema/client; do not merge its UTF-16 credential storage into generic UTF-8 storage. |
| `com.zerogamestudio.zeroengine.pathfinding2d` | 1.7.3 | 2022.3 | Preserve consumer graph/search/jump improvements and declare Physics 2D plus Tilemap. |
| `com.zerogamestudio.zeroengine.persistence` | 2.1.2 | 2022.3 | Preserve slot/participant/atomic promotion contracts; merge P6 typed dictionary payloads and isolate test storage. |
| `com.zerogamestudio.zeroengine.portrait-speech` | 0.1.1 | 6000.0 | Promote generic embedded source with original GUIDs; keep POB composition and production art in POB. |
| `com.zerogamestudio.zeroengine.preview` | 0.1.1 | 2022.3 | Keep adapter-driven preview lifetimes and explicit adoption plans; no production/save ownership. |
| `com.zerogamestudio.zeroengine.project-atlas` | 1.2.1 | 2022.3 | Keep navigation and catalog/document projections; different responsibility from release planning. |
| `com.zerogamestudio.zeroengine.pvp` | 1.0.1 | 2022.3 | Keep asynchronous snapshot/matchmaking/ranking contracts; distinct from real-time network sessions. |
| `com.zerogamestudio.zeroengine.qte` | 0.1.0 | 2022.3 | Keep caller-owned timing/input challenges; no direct input-device or gameplay-clock coupling. |
| `com.zerogamestudio.zeroengine.render` | 0.4.3 | 2022.3 | Keep portable pose/body/weather primitives and declare Particle System; no mandatory URP dependency. |
| `com.zerogamestudio.zeroengine.render.urp` | 0.4.2 | 6000.6 | Merge POB rim-light improvements without dropping P5 body/weather backends; preserve old default image response. |
| `com.zerogamestudio.zeroengine.rpg` | 2.3.1 | 2022.3 | Keep turn-order/BP/shield-break variants; declare directly used engine modules. |
| `com.zerogamestudio.zeroengine.settings` | 1.0.1 | 2022.3 | Keep transactional versioned player settings; legacy Persistence settings remain a compatibility API. |
| `com.zerogamestudio.zeroengine.social` | 3.0.0 | 2022.3 | Keep relationship transactions and notifications; Economy/Persistence participant contracts remain explicit. |
| `com.zerogamestudio.zeroengine.tce` | 0.2.1 | 2022.3 | Keep generic trigger-condition-effect authoring/runtime contracts; project effect bindings remain adapters. |
| `com.zerogamestudio.zeroengine.tce.presentation` | 0.2.0 | 2022.3 | Keep snapshot/afterimage/ghost presentation separate from TCE logic and gameplay state. |
| `com.zerogamestudio.zeroengine.ui` | 2.3.0 | 2022.3 | Keep view/layer/stack/toast contracts and optional loaders; retain P6 exact toast scheduling checks. |
| `com.zerogamestudio.zeroengine.world` | 2.1.0 | 2022.3 | Keep calendar/environment/map state; geometric FogOfWar and render output are separate adapters. |
| `com.zerogamestudio.zeroengine.world-shift` | 0.1.1 | 2022.3 | Promote generic embedded visual-transition source with original GUIDs; project world identity remains in POB. |

## Compatibility and adoption gates

- P6 still uses AutoBattle 1.0, Character 2.6 and Data Toolkit 1.0. AutoBattle 2.x
  intentionally removes the former manager/unit/skill/simulation API. Keep the
  existing P6 pins until its composition has been migrated and validated; copying
  deleted prototypes back into the new kernel would not establish compatibility.
- Multiplayer configuration keeps its original asset GUID and `MovedFrom`
  metadata after moving from Core into the Configuration assembly. Consumers
  referencing concrete config types must reference that assembly and verify old
  serialized assets before adopting the package combination.
- ModSystem manifest/contracts have one owner. Legacy parser, hot reload, Lua
  and Editor tools stay in the compatibility bundle, with original canonical
  GUIDs. Moved component assemblies require Unity serialized-data readback.
- Asset Catalog and Particle Catalog retain different data/credential contracts.
  Their credential blob encodings differ; a shared storage implementation needs
  an explicit codec/migration contract. The audit does not access or rewrite any
  real credentials.
- Legacy event/settings APIs remain compatibility APIs. Typed events and the new
  transactional settings service are separate contracts; projects migrate at
  their composition roots rather than having their saved data silently replaced.

## Verification boundaries

The static audit checks all source packages. Its executable checks and compatible
CI project selection are maintained in `Tools/Packages`; the CI generator keeps
Unity 6-only packages out of Unity 2022.3 lanes and includes the legacy dependency
closure. CI project generation is not an actual GameCI run.

Native Unity evidence currently includes 248 configuration cases, 86 Render
EditMode cases and 11 Render PlayMode cases with no failed cases. The initial
55-package import compiled without errors on Unity 6000.6.4f1. A later
Pathfinding run returned 144 successful cases, but its request exceeded its
budget while an Input System setup dialog blocked dispatch; that request remains
a budget failure with cleanup verified. Portable configuration/preset and other
kernel runs provide additional focused evidence, not Unity or Player acceptance.

Focused native results cover Data (64), persistence/atomic restore (7), P6 wallet
and JSON payload cases (7), Portrait Speech (28), World Shift (21) and Core
PlayMode lifecycle/PoolManager ownership (3), with no failed cases after the two
fixture/dependency corrections. Neutral ModSystem has 43 successful cases and
three legacy-presence checks skipped in the independent fixture. The original
historical GUIDs of both moved legacy components resolve through Unity to their
expected classes in `ZeroEngine.ModSystem.Legacy`; their metadata is preserved.
The final 14-package independent import compiles, and native tool actions open
the expected Portrait Speech and Multiplayer windows and restore their temporary
windows/selection. Source descriptor validation covers 11 modules and all five
CI installation combinations.
Additional native EditMode coverage includes Analytics (125), Extraction (215),
Feedback (37), tutorial contracts (28), Data Toolkit (26) and two corrected Toast
timing boundaries, with no failed cases after replacing the stale exact patch-
version assertion. Feedback PlayMode (3) and the full tutorial interaction
scenario (1) now succeed after the blank fixture receives its documented TMP
font prerequisite. The fixture uses imported SDK resources and a separate
dynamic font with source data; production settings and fonts are unchanged.
The source remains an unpublished candidate.

SDK-present SpineSkin and legacy Localization branches also compile against
frozen SDK binaries already installed on the development machine. Those checks
establish C# API compatibility, not native SDK runtime or Player acceptance.
Spine's namespace/attribute collision and missing SDK/UI references are corrected;
Localization's Editor references and installed-package define are connected.

A broad Core/legacy run is retained after a synchronous async-wait deadlock in a
persistence test; the corrected bounded-yield test now passes independently.
A separate full-combination request completed package resolution; its cleanup
probe encountered duplicate `JsonConvert` types from Localization and Newtonsoft.
The idle disposable Editor was normally closed after clean scene/status readback,
and that original request subsequently verified cleanup. The hung Core/legacy
request remains retained; normal close has not ended the unresponsive process.
These original operations are not replayed or reclassified as passing tests.
An imported wallet test also wrote the disposable fixture's default storage;
that file was preserved and the test now injects external persistence ownership.
Its content is unchanged after the corrected wallet and lifecycle checks.

No production consumer manifest, lock file, scene or asset has been changed by
this integration. Unity 2022.3, optional commercial SDK presence, Player/platform
behavior and real POB/P5 consumer acceptance remain unverified. Do not start the
native Console or the synchronized AI Skill until the package acceptance gate is
satisfied. This document records ownership and boundaries; active progress remains
in the existing development-content registry.


The first Linux CI run exposed a foreign-path validation gap in Project Atlas:
Windows drive-qualified paths must be rejected on Linux as well as Windows. The
1.2.1 candidate closes it. Its native focused suite has 52 successful cases.
Audio service activation/ownership tests now run in PlayMode (two successful
local cases), where OnEnable/OnDisable actually execute on both supported Editor
lines. Source-lifecycle checks follow the new achievement save adapter, and the
remaining scanner/label/boundary assertions match the current contracts without
requiring a fixed patch version, an installed networking SDK or CJK font metrics.

Read-only consumer API checks freeze actual Unity compiler inputs and rebuild the
candidate package assemblies outside the production projects. P5's 53 direct
production consumer assemblies compile against the candidate. Of POB's 51, the
first run found its currently adopted sort-layout contract missing; that contract
and repeated-input bulk-copy API are now restored from consumer pin `5066d01`.
The formerly failing POB Runtime assembly compiles against the repair, and the
other 50 original checks remain valid. Neither input set drifted during those
checks. The restored Stat kernel has 18 successful native focused cases.
These are API and package-behavior checks, not full consumer runtime acceptance.

The new minimal Core/Audio PlayMode lane also exposed Core's undeclared uGUI/UI
dependencies, which larger installations had supplied indirectly. Core 2.2.2
declares the packages and assembly it actually uses. CI keeps this three-sibling
runtime combination separate from the five EditMode installation combinations.
