# ZeroEngine.Interaction

Game-independent interactions with one explicit world scope and one input session per actor.
Version 0.2.0 adds a shared offline/server authority receiver while preserving the
existing Core/Unity entry points and single-player gesture semantics. POB adoption and retirement of the older
`ZeroEngine.Gameplay/Interaction` implementation are separate follow-up work.

## Boundaries

- `ZeroEngine.Interaction.Core`: pure C#, no Unity, POB, other ZE package, inventory,
  input-system or network dependency.
- `ZeroEngine.Interaction.Unity`: optional MonoBehaviour lifetime and distance adapters;
  caller-owned input, scaled/unscaled or injected game clock.
- `Samples~/InteractionPlayground`: optional uGUI consumer. No game assets, saves,
  default gameplay registration or automatic scene bootstrap.

The new `.Core` and `.Unity` namespaces do not collide with the existing
`ZeroEngine.Interaction` types in Gameplay. Both packages can be installed during migration.

## Installation

Pin a tested source commit in the consumer manifest:

```json
{
  "dependencies": {
    "com.zerogamestudio.zeroengine.interaction": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.interaction#<tested-commit>"
  }
}
```

The runtime package has no sibling UPM dependencies. The optional playground needs
`com.unity.ugui` and the UI module. Its generated EventSystem uses StandaloneInputModule;
projects configured for new-input-only should supply their normal UI input module.
No runtime interaction component polls a keyboard or creates an input singleton.

## Action contract

1. Create a world and register each available target. Retain/dispose the returned
   activation lease. IDs must be nonempty and unique within that scope.
2. Give each actor a unique ID, subject, monotonic finite clock, spatial query and
   maximum distance. Focus is selected by priority, then squared distance, then stable ID.
3. Forward input to `Press`, `Release`, `Cancel`, and call `Tick` from the intended
   gameplay update. `Press` is latched until release. Hold and repeat use the injected
   clock; one Tick produces at most one repeat after a frame jump.
   `Press` reports input/request admission, not purchase success. Observe `Completed`
   for the settled outcome, especially for delayed confirmation.
4. Implement side-effect-free `Check`. A missing business provider must return Deny.
   A target can remain focused while denied, allowing its UI to explain the reason.
5. `Execute` receives an `InteractionRequest`. Complete immediately or retain it for
   confirmation. Apply business state through `request.TryCommit(() => ...)`.
   The handle rechecks actor/target lifetime, focus, current spatial admission and
   conditions before allowing the mutation once. Use `TryDeny` or `TryCancel` to settle
   without mutation. Use `Cancelled` to release an action-owned reservation.

All methods, callbacks and completions belong on the owning gameplay thread.
The authority entry described below receives inputs through authenticated actor
bindings; its transport adapter supplies the session and player identity.

Focus changes, target unregister/disable, actor disable/disposal, scope disposal, explicit
cancellation and backwards clock resets invalidate pending work. A pooled target receives
a new activation; callbacks from its old activation cannot commit to the new one.
Releasing a hold cancels an outstanding hold/repeat request. A pending tap confirmation
survives release but is cancelled when its focus/lifetime ends.

`TryCommit` prevents a second invocation; it cannot roll back arbitrary partial side effects
if a business callback throws. Such a callback reports Faulted and remains settled.
Consumers own atomic wallet/reward transactions and reservation commit/cancel semantics.
For a provider that returns business acceptance, use the separately named
`TryCommitResult(() => providerAccepted ? InteractionCheck.Permit : InteractionCheck.Deny(reason))`.
A false/rejected business result settles Denied once. Existing `TryCommit(Action)`
callers, including assignment/expression lambdas, keep their original semantics.
Neither path can undo arbitrary partial provider side effects.
When `TryCommit` returns false, release any action-owned reservation; the cancellation
event covers automatic cancellation, while a business denial/fault still belongs to its caller.
Observers should not throw; state is finalized before completion notifications.

## Unity usage

Add an `InteractionWorldBehaviour` scope, `InteractionTargetBehaviour` targets and
`InteractionAgentBehaviour` actors. Author stable IDs when persistence/network identity
is required; omitted IDs are runtime-instance identities only. Bind the target's
`Checking`/`Requested` callbacks by composition. Custom integrations can implement
the core target/range interfaces directly. An unbound action denies
interaction. Bind input via the actor's public methods. Configure a custom game clock
before starting an input session. No Physics, singleton search, scene-manager or UI
dependency is imposed on the runtime bridge.

## Offline and network authority

`Core.Authority.InteractionAuthority` runs the same `InteractionAgent` for local
calls and authenticated remote actor bindings. The core still has no sibling,
Unity, transport or networking SDK dependency. The optional
`com.zerogamestudio.zeroengine.interaction.multiplayer` module maps the existing
ZE `NetworkEntityKey` identities to this receiver and supplies an isolated
offline/host/client playground.

An authority owns one nonzero epoch. Its actor binding and target lease expose
activation identities; a command cannot select a receiver from a claimed actor
ID. The transport routes to its authenticated binding. `Press`, `Release`,
`Cancel` and optional `Confirm` commands carry positive, strictly increasing
64-bit sequences. A press's action ID is its first sequence. Sequences never
wrap or reset inside a binding; reconnect creates a new binding activation.
The authority evaluates hold/repeat on its supplied game clock and checks the
explicit requested target, rather than redirecting the request to another
nearby object. Caller focus changes send Cancel; server range, availability and
conditions remain authoritative.

Matching retries return a cached admission receipt with the latest retained
outcome. Reusing a sequence for a different command is rejected. Evicted old
commands remain stale and can never execute again. Receipt `Handled` and
`InputAdmitted` are not business success: observe the settled outcome and the
authority's domain state. Bounded outcome notifications are transient; older
state is recovered from domain snapshots, not by replaying gameplay.

Local input cancellation and authoritative settlement are separate. Cancel
before a deferred commit invalidates it; cancel after a completed commit never
undoes it. `InteractionOutcomeMirror` accepts a late valid success even after
local focus/input cancellation and ignores stale actor/epoch/revision data. It
never grants an item or charges a wallet on a client. A registered confirmation
callback belongs to the authority and must use the guarded request to commit.
Reservation refund/commit, shared versus personal loot and atomic reward/cost
transactions remain provider responsibilities.

These are live-session at-most-once guards, not durable distributed transactions.
Session restore, connection authentication, ordered/reliable delivery and POB's
FishNet/protocol mapping retain their existing owners. Scope or actor disposal
cancels pending work; rebinding and pooled targets cannot inherit old requests.
All authority and business calls stay on the owning gameplay thread.
Commands received reentrantly during an input dispatch or authority Tick return
Busy without consuming their sequence. Queue those inputs until the current call returns.

## Playground and verification

Import **Interaction Playground** from Package Manager, then open the included
`InteractionPlayground.unity` scene. The example adds no global Editor menu. It covers:

- independent actor wallets and focus;
- tap-to-toggle door and tap-to-talk / hold-to-gift NPC;
- pickup once per activation, with explicit pooled respawn;
- delayed shop confirmation, revalidation and cancellation when leaving range.

The package requires Unity 6000.6 or newer. The included scene was authored in
Unity 6000.6.4f1. Its UI and interaction objects are created at runtime.

The repository's isolated `interaction` lane imports only this package and this sample:

```powershell
python Tools/Packages/create_test_project.py --destination <new-test-project> --lane interaction --unity-version 6000.6.4f1 --test-framework 1.8.0
```

Run `ZeroEngine.Interaction.Tests` EditMode contracts, Runtime lifecycle tests and
`ZeroEngine.Interaction.Samples.Tests` PlayMode example tests using the project's normal
Unity test route. Test projects are disposable and must not use production save paths.

The 0.1 baseline verification on Unity 6000.6.4f1 executed 33 EditMode contracts and 9 PlayMode
lifecycle/example cases with no failures or skipped cases. A composited 1280×900
Game View capture was inspected and PlayMode/background state was restored.
The isolated CI lane uses the same Unity 6000.6.4f1 baseline.

The 0.2 authority candidate was checked on Unity 6000.6.9f1 with 62 core EditMode
contracts, four optional replication-binding contracts and 12 PlayMode lifecycle/
sample cases. This includes the original 33 core and nine PlayMode cases,
gesture-trace comparison against the direct agent, explicit provider rejection,
reentrant-input protection and transport-worker cleanup. The independent .NET
example additionally exercises ten scenarios in separate host/client processes,
including server-clock pause/resume and release without a trailing tap. These
package checks leave POB's real consumer regression to its integration step.

## Existing interaction review and later migration

Audit baseline: ZeroEngine source `adb92428122ffbd4f35c9ede9df884e29fc5259a` and POB's
current `POBPickup` / `CoreComponents/Interaction` consumers. Findings describe code,
not an assertion that every issue is a currently observed player-visible failure.

| Existing boundary | Finding | New ownership / migration requirement |
| --- | --- | --- |
| Gameplay `InteractionManager` | One global focus target shared by detectors | Per-actor focus in an explicit scope |
| Gameplay `InteractionDetector` | Reads input and Unity time directly; no complete hold/repeat/cancel session | Caller input/clock ports and explicit session lifetime |
| Gameplay conditions | Item/quest conditions can default to true without their integration | Explicit check providers; absent required provider denies |
| Gameplay outline | `UpdateOutline` contains a placeholder | Keep presentation consumer-owned; provide no pretend outline capability |
| POB `Interaction` | Branches on NPC type to implement gift acceleration and dialogue fallback | Game-owned action policy layered on reusable gestures |
| POB `POBPickup` | Mixes prices, health costs, UI, room doors, physics and regional activity | Extract interaction mechanism; retain these gameplay/presentation adapters in POB |
| Pending purchase | Delayed actions need current-owner validation and cancellation | Activation-scoped request and guarded business commit |

Do not move POB's complete pickup hierarchy into the package. Before POB adoption,
map its local-player input, game clock, purchase reservations, NPC hold behavior,
room-transition completion and authoritative multiplayer contracts explicitly. Exercise
representative pickups in that consumer, preserve serialized identities and then retire
its replaced mechanism. Inventory legacy consumers before replacing old Gameplay types;
Tutorial and Command have separate ownership and are not removed by this package.
