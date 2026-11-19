# ZE native Console — G4 protocol and vertical slice

The [G3 package baseline](../packages/2026-11-10-g3-acceptance.md) is frozen at
`8441097`. This stage implements one native execution pipeline before migrating
the project commands. Production project dependencies and Quantum assets keep
their current owners. Source implementation is not consumer adoption or release.

## Package boundaries

| Package | Responsibility |
| --- | --- |
| `com.zerogamestudio.zeroengine.console` | Pure C# descriptors, registration, parsing, binding, policy, execution, completion and bounded session state; a separate Unity assembly supplies main-thread dispatch and lifecycle hosting. No dependency on QC or gameplay packages. |
| `com.zerogamestudio.zeroengine.console.ui` | Independently usable native lightweight uGUI frontend, history, output and completion presentation. Project font/input isolation can be supplied explicitly. |
| `com.zerogamestudio.zeroengine.console.quantum` | Optional verified QC frontend. Uses public SDK APIs, owns its UI instance and forwards raw input to the native pipeline. Vendor code/assets do not enter ZE. |

Bare gameplay packages remain installable without Console. Additional module
commands use explicit optional composition rather than changing every module's
dependencies. Project resource IDs, singletons and business rules remain project
code. The AI Support package follows G7 after the catalog and API have stabilized.

## Core contract

- Descriptors carry a stable ID, primary name, aliases, module/source identity,
  help/examples, parameter schema, environment/capability policy and version.
  Registry publication is atomic per module. Duplicate IDs or names reject the
  whole registration; disposal removes discovery and cancels owned invocations.
- An explicit handler receives typed arguments, a caller context, output sink
  and cancellation. Both direct invocation and command lines traverse the same
  policy and argument binding. The kernel has no Unity, QC or project references.
- Parsing preserves source spans, empty quoted arguments, Unicode, escaped
  quotes, negative numbers and bracketed values. Conversion is invariant-culture;
  native/project converters own engine values, enums and resource identifiers.
  Invalid syntax/arity/conversion has a stable error code and argument location.
- Results distinguish success, failure, cancellation and deadline expiry, with
  optional data and bounded output events. A `void` legacy handler is not given a
  fabricated business-success result. Cancelling never claims rollback of an
  already applied side effect or replays a command after frontend failure.
- Listings and completion apply the same policy as execution. Completion returns
  stable candidate identities and source replacement ranges. Dynamic providers
  support cancellation/invalidation; no whole-project asset scan per keystroke.
- History, output, pending requests and completion have explicit capacity limits.
  Unity hosting dispatches engine work on the main thread and releases input,
  subscriptions and module handles on close/disable/unload.

Registration is explicit and AOT-compatible. Optional authoring attributes or
Editor-generated registries must produce repeatable static calls before a Player
build; Player startup does not scan all assemblies or compile dynamic code.

## Quantum feasibility boundary

Installed-source inspection confirms that `OnInvoke` fires after QC execution.
The public response API can deliver raw input before QC parsing, but response
mode suppresses QC's own suggestion UI and has no public general end-response
operation in the inspected installation. Therefore a response bridge must own
its cloned QC instance, rearm through public lifecycle callbacks and supply native
completion presentation; it must not leave borrowed QC instances in response mode.

`Auto / Native / Quantum` selects a single active frontend and input lease. Auto
uses QC only with a verified compatibility profile and usable presentation;
otherwise it selects Native and emits one specific reason. Unknown SDK versions
are not assumed compatible. Physical SDK absence, uninstall, callback disposal,
quotes, async output and no-double-execution are required slice checks.
Reflection, if needed for optional SDK isolation, is restricted to public members
with explicit preservation/verification. No private-field patching, vendor-source
changes or fake vendor types are allowed.

## Compatibility inventory and slice exit

The current POB catalog has 52 contracts with 157 distinct aliases, plus the
separate `pob-death-blood` command: 158 total. Read-only extraction uses the
project's actual catalog parser and frozen source hashes, yielding 99 distinct
parameter strings. This is a signature baseline, not yet the complete migration
decision for policy, completion, side effects and output.

G4 first proves a read-only query, a project-owned write, dynamic completion and
an asynchronous/cancellable operation through the same pipeline. Native must run
with no QC installation. QC either passes the same checks or receives a concrete
compatibility limitation before broader migration. Check domain reload, duplicate
opening, module disposal, focus/input restoration and the declared AOT path.
Record a comparable QC/Native baseline before making cost or superiority claims.

Only after that vertical contract is verified does G5 migrate all inventory
entries, followed by G6 experience/cleanup and G7 synchronized Skill delivery.
The immutable package baseline remains independently reviewable in its own PR.

## Quantum source comparison and remaining parity

The comparison uses the installed Quantum Console 2.6.7 source, including its
processor, parser, suggestor, serializer, actions, registry and UI. Vendor source
and assets remain outside ZE. Existing project commands and the Quantum default
entry remain available while native capabilities are developed independently.

| Area and Quantum source | ZE behavior and remaining work |
| --- | --- |
| Command discovery: `QuantumConsoleProcessor`, scan rules, `InjectionLoader` | Explicit, atomic module registration avoids a Player assembly scan. Optional attribute/generated authoring remains future work. |
| Catalog cache: `GetAllCommands` | Reuse a sorted registry catalog until registration changes; recheck live caller policy for every discovery/completion request. |
| Parameters: `CommandData`, `QuantumParser`, parsers | Quotes, Unicode, spans, optional/variadic arguments, scalar/nullable values, arrays, standard generic collections/dictionaries and bounded opt-in arithmetic exist. Host defaults add Unity vectors, quaternions and colors. Explicit overload families select by arity/conversion without probing handlers; ambiguous calls require a stable ID. Generic aliases use explicitly closed, constraint-checked types. Named object references use a scoped converter. Arbitrary runtime type discovery and tuple parsing remain open. |
| Extensibility: parser/suggestor/serializer injection | Explicit converter/completer leases, conversion fallback and bounded result formatter extension exist. Avoid unbounded caches of user values or reflective object inspection. |
| Suggestions: `QuantumSuggestor`, suggestion stack/display | Fragments, aliases, prefix priority, resource labels, quoting, insertion caret, clickable candidates and active-parameter/signature guidance exist. Natural ranking remains a separate improvement. |
| Results: `QuantumSerializer` | Data-only Unity Host results use invariant bounded formatting for scalars, dictionaries, tuples, sequences and Unity values. Explicit messages and structured status/data retain precedence and identity. |
| Async/actions: `QuantumConsole`, `ActionExecuter` | Task/ValueTask observation, main-thread dispatch, cancellation, deadlines and single-use ValueTask sources are covered. Commands can sequence numbered choices and confirmations through the same policy-checked invocation in either frontend. |
| Macros/scripts: `QuantumMacros`, preprocessors, external command runner | Explicit per-session macros have bounded definitions/expansion, cycles fail before dispatch, and expanded commands retain normal policy. Caret hints follow expanded parameters; completion preserves the raw macro/suffix and rejects replies after definitions change. External scripts and arbitrary code execution are not enabled. |
| Object targets: `InvocationTargetFactory`, `QuantumRegistry` | Projects bind values or live project-owned resolvers by name and lease. The same registry feeds typed conversion and completion with optional validity checks. Projects own target visibility and business permissions. |
| Logs/history: `LogStorage`, `LogQueue`, `QuantumConsole` | Bounded output/history, thread-safe writes, reusable output buffers, search, copy and follow controls exist. History duplicate policy and richer log formatting remain distinct improvements. |
| UI/input: theme, localization, key config and UI controllers | Lower-left translucent panel, icons/tooltips, configurable area and font size, common keys and restored input exist. Runtime theme/localization profiles and drag/resize are not equivalent yet. |
| Mobile/IME | Safe area, keyboard-area fitting and composition guards exist; physical phone keyboard/IME behavior still needs its own evidence. Layout previews do not establish device parity. |
| Release access | Shared Editor/development access and configured hidden release unlock exist. No universal shipped password; projects supply the unlock policy. |
| Dependencies and portability | Core has no Unity/QC reference; UI and Input System integration are separate. A Godot frontend does not yet exist. |
| Performance/build cost | Compare matched workloads and artifacts separately for cold startup, execution, completion, allocations, UI/frame time and stripped Player size. Source size or one microbenchmark does not establish overall superiority. |
| SDK removal | Keep the optional adapter, typed project backend and serialized resources separate. Removing a backend in a test does not prove physical SDK/package uninstallation. |

Useful Quantum behavior is tracked above even when it is optional rather than
enabled in every game. Native replacement is not complete until the applicable
gaps and consumer checks have evidence; a successful default Quantum build does
not establish native feature parity.

## Verified Editor slice, 2026-11-11

The optional Quantum package now uses a source-verified 2.6.7 profile, public
response/lifecycle APIs and cloned widgets. Native completion/history, output
clear revisions and cancellation remain owned by the same session. The selector
releases the previous input/EventSystem lease before opening its replacement,
falls back on a rejected profile or opening failure, and never replays a command.
The project may assign its own TMP font to cloned QC text widgets.

| Focused check, Unity 6000.6.4f1 | Executed | Failed |
| --- | ---: | ---: |
| Native kernel, including bounded output and clear snapshots | 23 | 0 |
| Main-thread dispatch and host lifecycle | 3 | 0 |
| Native UI, paused cancellation, selection and opening fallback | 5 | 0 |
| Verified QC: quoted Chinese completion, one execution, paused cancellation, switch to Native; unverified-profile fallback | 2 | 0 |
| Physical SDK removal: adapter import, no loaded vendor assembly, Native execution and no missing components | 1 | 0 |
| Package audit/project-selection Python contracts | 18 | 0 |

Every local Editor request above reached a terminal result with native cleanup
confirmed. Both frontends' actual 1920x1080 Game View output was inspected after
adding an owned clearing camera and a transient CJK font to the isolated fixture.
Initial missing-glyph and stale-completion findings were fixed; assertions alone
were not used as visual acceptance. Vendor code, licensed assets and temporary
fixtures stay outside this repository.

This Editor checkpoint did not establish Player compatibility. The Editor
profile deliberately refuses a different Player assembly identity. The final
Player support boundary is recorded below; physical target IME acceptance remains
unverified. The recorded Editor cost sample
includes the same 40 ms completion debounce and is diagnostic only. No G5 bulk
migration, G7 Skill delivery or production adoption is claimed.

## Managed SDK-absent Player slice, 2026-11-11

Managed build `job-4fa8445022`, attempt
`attempt-7e023895fb2e4e291dae69b8c9b00e56`, ran on Windows Worker 01 / 3060 Ti
with Unity 6000.6.4f1. All three Console packages were loaded as Git packages at
`e47ce2499d9c65f3a4b7df10c37d8371ba1d7458` according to actual PackageInfo.

| Player configuration | Build and execution | Established behavior |
| --- | --- | --- |
| Mono Development | Succeeded / PASS | Main-thread write exactly once, array/enum conversion, quoted Chinese completion, cancellation, SDK absence, Native fallback and input/EventSystem/cursor restoration. |
| IL2CPP Development, configured High stripping | Succeeded / PASS | The same behavior on the compiled AOT path. |
| IL2CPP non-development | Succeeded / PASS | SDK absence and the default-disabled Console host; no development command ran. |

The result and artifact-index digests match the managed terminal manifest; all
796 indexed files were read back and hashed, and all Player logs had no matching
exception/assertion failure. No Unity process, build/CI ticket or attention drain
remained. This was a headless structural/lifecycle check, not rendered UI or IME
acceptance. No SDK-present QC Player compatibility is inferred from fallback.

The fixed managed entry is qualified by ContentBuilder source
`2e2538c974fbb701c9d57e1e4197a6f3073c0629`, Native CI
`ci-7a1b689a7c9baac8d6be94ac` and the finalizer's exact-source receipt. The
Windows runtime uses signed counter 147. The independent `console-quantum`
package CI lane also passed without vendor SDK. Production consumer pins and
scenes retain their existing owners.

## Controlled composition regression, 2026-11-11

The user selected CLI/PlayMode validation rather than desktop takeover. A real
PlayMode UI event with a controlled `BaseInput` exposed premature execution in
both frontends: their guards read the global legacy composition string while
the active EventSystem supplied a different composition source. Both now use
the current module's active input provider, with the prior legacy fallback.

The same private SDK-present Native/QC fixture now records zero calls while
composing, one call with committed `你好`, and restored input/cursor ownership
on close (2 cases). The package's six Native UI cases also pass, including a new
Run-button regression using the same public EventSystem input contract. Every
request reached terminal cleanup. These are controlled composition and submit
events; no physical Windows IME or desktop control is claimed.

The corrected package commit `e4c028a846abcecd2b7a009847c617d4fe8aeb71`
passed all nine public Unity CI lanes and the package boundary job in run
`42962884292`. Managed build `job-55a1ecc89b`, attempt
`attempt-6722b29acd407a2dd36e362ef6e57174`, then built and ran the same three
Player configurations on 3060 Ti. Both development backends recorded zero
composition-stage calls, one committed `你好` call and restored input ownership;
the non-development host remained disabled. All 796 indexed files matched their
digests and the terminal manifest, with no matching Player exception/assertion
failure or remaining Unity process/build lease.

The CLI-only fixture is qualified at ContentBuilder source
`7ddc547ae2988f696e715df2c9be3c7c5d786de2`, Native CI
`ci-59f27941a6c7a07c730f95a9` and its finalized receipt. Windows signed counter
148 was active at that checkpoint; the Control update changed only the fixed Git source contract.
The maintenance drain was released at generation 243. SDK-present QC Player
identity remains an explicit compatibility boundary: an unmatched profile uses
Native. Consumer migration, experience acceptance and Skill delivery retain
their later stages.

## Target-compiled QC profile, 2026-11-11

The SDK-present Player candidate now has an explicit build-time authoring path.
An Editor-only metadata probe compiled the actual QC SDK for the Windows Player
and read a different MVID from the loaded Editor module. The verifier reads the
compiled module without loading another vendor assembly into the Editor domain,
then stamps target/backend/Unity version and the exact module identity. Runtime
matching retains that exact identity check; it rejects Editor profiles in Players
and Player profiles in the Editor. No blanket AOT exemption is introduced.

Four private metadata/precompile cases, five SDK-absent profile guard cases,
the two existing SDK integration cases and two controlled composition cases
passed with native cleanup confirmed. The following final-linked matrix
supersedes the initial precompile-only Player profile attempt.

## Final-linked Player support and bounded G4 exit, 2026-11-11

Managed job `job-7855a75b17`, attempt
`attempt-d20679dc1f0439f5f13e26487a2bca9a`, built and ran all six configurations
on Windows Worker 01 / 3060 Ti with Unity 6000.6.4f1. Actual PackageInfo in every
build records all three Console packages at Git commit
`84d523eb4946d3db8cc03fc6705ac873b42ac0fd`; its public CI run `42987324184`
completed all nine Unity lanes and the package boundary check.

| SDK / Player | Runtime result | Established boundary |
| --- | --- | --- |
| Absent / Mono Development | PASS | Native execution, controlled composition and restored input. |
| Absent / IL2CPP Development | PASS | The same Native behavior with High stripping. |
| Absent / IL2CPP non-development | PASS | Host disabled by default; no command invoked. |
| QC 2.6.7 / Mono Development | PASS, actual Quantum frontend | Two builds bind the final linked DLL; module identity remains stable. Completion, cancellation, one committed invocation, frontend switching and restoration are verified. |
| QC 2.6.7 / IL2CPP Development | UNSUPPORTED; matrix FAIL | Runtime `ModuleVersionId` is all zero while the valid linked profile has a nonzero ID. The strict guard selects Native; its completion, cancellation, controlled composition and restoration are independently verified. |
| QC 2.6.7 / IL2CPP non-development | PASS | SDK is present; host remains disabled and no command runs. |

The managed job deliberately remains **failed** for QC IL2CPP compatibility.
Native fallback never substitutes for a Quantum success. This is a concrete
unsupported-platform conclusion permitted by the G4 exit, bounded to this exact
Unity/SDK/Windows combination. Other combinations require fresh evidence.

Unity's linker changes the precompiled DLL identity. The build therefore creates
a provisional profile, builds a probe, verifies the final linked assembly,
updates the same owned profile asset and rebuilds. Assembly name, MVID and PE
SHA-256 must remain stable across the two builds; Mono also checks the shipped
DLL against the linked bytes. The [Quantum setup](../../com.zerogamestudio.zeroengine.console.quantum/README.md)
documents this workflow. No vendor source changes or empty-MVID exemption occur.

All **1,661** persistent artifact files matched the terminal manifest's index
and result digests. A previous job's index had included compiler scratch later
removed by Unity; signed runtime counter **155** excludes only that temporary
precompile directory. The original failed job and its evidence remain intact.
The source fix is ContentBuilder `9b415c3ad65a22189bc6b0165f1388f8dd855831`;
its 14 focused audit checks, Ruff and both required local runtime gates exited
zero. New full Native CI is separate and is not claimed here. The runtime retains
all 694 other signed baseline files and the existing build profile.

Final readback found no Unity process or active Worker job. This task's physical
maintenance reservation was released at generation 59833. No publish or
notification was requested. The six-config evidence establishes the bounded G4
exit and permits G5 migration; project adoption, G6 experience/cost acceptance,
physical OS IME acceptance and G7 Skill delivery remain separate.

## Native project migration checkpoint, 2026-11-12

The POB working workspace now uses an explicit 161-entry native declaration
catalog, schema 1, with declaration digest
`b29a7ea99a944aedc65aff91c0b8a9ba134bd9fc6ded656fa629b44149d3060d`.
Original handlers, aliases, defaults and business guards remain the authority.
Legacy void/string handlers return `Unconfirmed`; fire-and-forget operations
return `Started`. Native help, completion, typed automation and text entry share
the same session and policy. A transient project panel owns pause/input lifetime.

All three Console packages in this scoped consumer verification resolve to
`038ed1772f775594faac522d17929a5d2b7993a3`. Other package pins and local trials
remain separately owned. GitHub run 43070638035, attempt 2, completed all nine
Unity installation lanes and the package-boundary check. Real isolated POB
PlayMode verified the original god-mode state change and restoration, one submit,
completion, honest cancellation/results, native panel reopening and navigation,
cursor, selection and time restoration. The normal saves and audio state were
restored after PlayMode exit.

G6 removes the old QC command-table/suggestor dependency from POB's debugging
assembly while retaining the licensed SDK and optional presentation asset.
Actual Windows Player script compilation produced 246 assemblies per mode.
Target-aware development links cover 131 original methods; release links contain
zero development entries. This is compiled target-metadata evidence, not a full
POB IL2CPP Player execution. The project extracts the verified SDK widget from
its old initially-transparent panel wrapper, so the optional QC clone is actually
visible. The native and QC controls, completion, shared session and restoration
were inspected in a 1920x1080 H.264 recording with a measured 12-second wall clock.
Sixteen warmed help invocations retain `Unconfirmed`; their Editor timings are
diagnostic. The allocation counter returned no change for a known managed
allocation and is explicitly unavailable, not a zero-allocation result.

The optional AI Support package supplies a read-only registered-package snapshot,
version-aware project Skill and explicit CLI synchronization. Fourteen isolated
CLI checks cover ownership, stale snapshots, version/repository distinctions,
local trials, downgrade/schema conflicts and write recovery. Its exporter and a
complete project-local synchronization were also exercised with the actual
Editor PackageInfo. POB adoption and exact source submission remain in progress;
human experience acceptance, physical OS IME and user release stay separate.

## Lower-left Native presentation candidate, 2026-11-13

The consumer's stable baseline remains POB `cs19159`, with its 41 public ZE
packages pinned together at `adb92428122ffbd4f35c9ede9df884e29fc5259a`.
The original Quantum entry and command table remain the default. This iteration
changes the independently selected Native view; it does not batch-migrate new
project commands or upgrade Unity dependencies.

The Native window now opens at the lower left with a bounded, Canvas-fitting
layout, clear/copy controls, font sizing, a scrollbar and an explicit output
follow switch. Completion candidates can be selected beyond the first eight
rows without executing a command. Scrollback displays up to 256 retained session
entries and no longer adds a 1,024-character truncation to each message.

Ten focused UI PlayMode cases executed in the isolated POB consumer with zero
failures or skips and confirmed native cleanup. They include the new placement,
long-output/reading-position and paged-completion contracts, alongside existing
composition, cancellation, fallback and input-lease checks. A subsequent real POB
flow exposed a registration collision when Quantum already owned `ConsolePanel`.
The consumer candidate gives Native its own appended panel ID and prepares it
through its owner, leaving the original registered Quantum panel intact. The
legacy panel loader only accepts a supplied panel when its key matches the
requested key.

The corrected Intro-to-Base flow passed with Native's 156 project-visible commands,
one input invocation, completion, reopening and input/cursor/time restoration.
The default Quantum route retained 155 aliases and 344 overloads before and after
Native use. Both actual Game view images were inspected; save/audio restoration,
PlayMode exit and native cleanup were confirmed. This is an internal candidate;
fresh public package checks and exact consumer adoption remain separate gates.

## Combined consumer compatibility, 2026-11-13

The later consumer fix uses a transient, non-registered Native panel instead of
the proposed appended panel ID. It preserves Quantum's registered panel and
uses the existing panel stack for pause/input restoration. With Quantum present,
the default entry opens Quantum; a missing or disabled widget falls back to
Native, and the next opening retries Quantum after recovery. This supersedes
the earlier consumer-only panel-key proposal without renumbering existing keys.

The combined source also retains the ZOS-1091 command-fragment fix: command names
and aliases can match a case-insensitive substring, prefix matches rank first,
and parameter completion and invocation remain unchanged. All 28 kernel cases
passed in the isolated consumer. A fresh real Intro-to-Base PlayMode flow passed
Quantum default, Native fallback, `weapon` to `pob-add-weapon` insertion without
execution, lower-left placement, and restored Quantum priority. Both actual
Game frames were inspected; input/cursor/time, save/audio, PlayMode exit and native
cleanup were confirmed. Package/consumer publication remain separately recorded.

## Lightweight presentation revision, 2026-11-13

The lower-left window now uses a bounded 720 × 400 reference layout, rounded
procedural UI meshes, clearer input focus and an empty-output hint. No image,
font asset, animation library or third-party runtime was added. Input System
integration lives in an optional assembly, gated by the installed package and
active input setting. The native UI assembly itself references neither Input
System nor Quantum; legacy input remains its fallback.

All 10 native UI PlayMode cases passed after fixing the new graphic's required
CanvasRenderer. The fresh POB gameplay flow retained Quantum priority, transient
Native fallback, one command invocation, fragment insertion without execution and
full state restoration. Both rendered Game frames were inspected. A separate
clean project resolved and compiled without Input System or Quantum; the optional
input assembly was absent and the native UI loaded successfully. Its temporary
Editor closed normally without unsaved scenes. These checks establish dependency
and interaction compatibility, not a comparative CPU or allocation benchmark.

## Icon toolbar revision, 2026-11-13

Common controls now use procedural line icons for clear, copy, font size,
output follow, execute, cancel, close and completion navigation. Chinese action
descriptions and shortcut hints appear on hover. Ready/running and follow/paused
retain distinct visual states. The shared hint stays inside the window, leaves
input focus alone and clears on close; command, completion and log text remain
text. No sprite sheet, icon font or package dependency was added.

All 11 UI PlayMode cases passed, including icon-only actions, hover explanations,
hint bounds, focus preservation and follow-state persistence across reopening.
The fresh real POB flow also passed Quantum priority, Native fallback, one submit,
completion insertion and restoration. The icon window, copy hint and recovered
Quantum Game frames were inspected. Consumer adoption remains a separate step.

## Font adaptation and comparison, 2026-11-16

Native and the shared completion overlay now reuse a lazy OS font family chain
without bundling font files or an atlas. An assigned project Font is borrowed
unchanged, with its existing fallback rules; only console-owned labels change.
The package never edits font names, materials, import settings, TMP assets or
global fallback tables, and never destroys a borrowed Font. ConfigureFont updates
an open view without resetting its draft/history. FontProvider resolves the
project's active language once per open; unavailable providers retain the last
valid font and unloaded fonts fall back on reopening.

The final isolated Windows PlayMode run covered 55 Host/UI cases, including seven
font lifetime/configuration regressions. A separate native PlayMode case checked
actual system/project glyphs and three 1920x1080 Game View frames, with unchanged
project font assets, restored settings and verified native cleanup. The existing
Liberation Sans face and the default OS chain rendered the desktop CJK/Latin
acceptance samples. This establishes this machine's glyph coverage, not universal
language support: complex-script shaping, RTL, color emoji, physical IME, WebGL
project font coverage and mobile devices retain explicit verification gaps.

The current comparison records narrower dependencies, smaller unstripped Editor
DLLs and the preceding matched core benchmark advantage. It does not establish
whole-Player size, startup, frame cost or full Quantum feature parity. Macros,
interactive action/coroutine workflows, object addressing, grammar extensions and
a Godot frontend remain outside the implemented native baseline. POB keeps
Quantum and its existing commands. Consumer package/Skill consolidation waits
for the Motion/TCE Presentation owner's tested commit and handoff.

Evidence: D:/CodexArtifacts/pob-ze-cohort-fonts-20261116/verification.json and
comparison.md. The earlier supported API/core benchmark evidence remains in
D:/CodexArtifacts/ze-console-polish-20261116; no new Player result is inferred.

## On-demand host and closed view, 2026-11-19

On-demand hosts defer the command session and frontend selection until use.
Native's release-on-close mode returns its Canvas, rows and owned font while
retaining the session, draft, caret and search state. The optional Quantum
frontend routes confirmation through the public response API and mirrors output
with the shared reusable buffer and bound SDK log delegates.

In Unity 6000.6.9f1 on Windows, 16 fresh hosts per mode used the same native view
and borrowed font inside a warm Editor. Median host activation was 0.167 ms
with eager initialization and 0.008 ms with on-demand initialization. The
closed views retained 1 and 0 Canvases respectively. Reopen medians were 0.42 ms
with the cached view and 6.96 ms when rebuilding it. The isolated suite passed
157 EditMode and 59 PlayMode tests. Evidence is recorded at
`D:/CodexArtifacts/ze-console-ondemand-20261119`.

## Matched Editor comparison, 2026-11-19

Unity 6000.6.9f1 PlayMode alternated twelve fresh pairs in the same isolated
scene. ZE created its panel on demand and released the view on close. Quantum
Console 2.6.7 used the installed SRP prefab with POB's initialization settings.
Both used 161 matching command names for the timed samples. Each run opened the
view, entered the same prefix, accepted one suggestion, ran the same command
once, cleared output, and closed the view.

| Median | ZE before tuning | ZE optimized | Quantum in the optimized run |
| --- | ---: | ---: | ---: |
| First activation of a fresh panel | 17.91 ms | 12.46 ms | 0.84 ms |
| Enter prefix, accept completion, and execute | 42.06 ms | 1.51 ms | 0.60 ms |
| Retained panel hierarchy after close | 16.0 KiB | 15.9 KiB | 111.6 KiB |

The startup measurement includes the first Canvas update. Quantum's catalogue
was ready for the twelve-pair comparison. In a separate run from a fresh
PlayMode domain, the first activation reached an interactable input in 176 ms
for ZE and 223 ms for Quantum. Each then ran the command once. The cold case
is one observation per frontend.

Memory covers the owned Unity object hierarchy. The test excludes shared assets
and the Editor process heap.
Command-name lookup runs on the next update, while argument providers retain
their debounce interval. The native panel builds inactive and creates
completion rows as needed. The isolated package suite passed 159 EditMode and
60 PlayMode cases. The test reports and raw measurements are recorded at
`D:/CodexArtifacts/ze-console-editor-parity-20261119`.
