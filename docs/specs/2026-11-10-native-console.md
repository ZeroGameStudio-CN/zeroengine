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
