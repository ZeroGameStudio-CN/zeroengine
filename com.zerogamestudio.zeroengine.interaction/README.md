# ZeroEngine.Interaction

Game-independent interactions with one explicit world scope and one input session per actor.
Version 0.1.0 is a package-first candidate. POB adoption and retirement of the older
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

All methods, callbacks and completions belong on the owning gameplay thread. This is
not a network authority implementation. A transport adapter must perform server-side
validation and map its own player, epoch and request identity before committing.

Focus changes, target unregister/disable, actor disable/disposal, scope disposal, explicit
cancellation and backwards clock resets invalidate pending work. A pooled target receives
a new activation; callbacks from its old activation cannot commit to the new one.
Releasing a hold cancels an outstanding hold/repeat request. A pending tap confirmation
survives release but is cancelled when its focus/lifetime ends.

`TryCommit` prevents a second invocation; it cannot roll back arbitrary partial side effects
if a business callback throws. Such a callback reports Faulted and remains settled.
Consumers own atomic wallet/reward transactions and reservation commit/cancel semantics.
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

Local verification on Unity 6000.6.4f1 executed 33 EditMode contracts and 9 PlayMode
lifecycle/example cases with no failures or skipped cases. A composited 1280×900
Game View capture was inspected and PlayMode/background state was restored.
The isolated CI lane uses the same Unity 6000.6.4f1 baseline.

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
