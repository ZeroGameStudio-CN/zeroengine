# ZeroEngine Game Feel

An optional, pure-System feedback coordinator with reusable channel policies.
The same attack can produce one global camera/stop contribution, separate local
reactions on each target, and a richer audio batch for multiple distinct contacts.
Duplicate collision callbacks do not become additional hits.

The runtime has no Unity or package dependencies (`noEngineReferences: true`).
It does not change damage, gameplay events, time scale, assets or devices. This is
unrelated to `ZeroEngine.Feedback`, the player issue-reporting package.

## Installation

Use the official UPM repository and a commit validated with the consumer:

```json
"com.zerogamestudio.zeroengine.gamefeel": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=/com.zerogamestudio.zeroengine.gamefeel#<tested-commit>"
```

Reference `ZeroEngine.GameFeel` from the consumer assembly. A local `file:` pin is
only a temporary integration tool; restore a tested Git pin before handoff. Add
the package to a Unity project's `testables` to discover its NUnit contracts.

## Choose identity and scope once

```csharp
var global = new FeedbackKey(attackId, pulseId);
var contact = new FeedbackKey(attackId, pulseId, targetLifetimeId);
```

- An attack/pulse identifies the actual event, not a physics callback. Distinct
  attacks and successful continuous-damage pulses get distinct IDs.
- A coordinator instance owns a channel and scope (view, target, audio batch or
  device). Channels never spend each other's admission or share an ambient cooldown.
- Use the two-argument key for an attack's global output. Use a nonzero `ScopeId`
  when one shared batch must distinguish target contacts. All three fields retain
  their full width; scope is not squeezed into a hash of the other fields.
- `FeedbackScopeIds<T>` is an optional weak object/lifecycle adapter. Call
  `Release(target)` on pool disable and `Reset()` on world/session exit. Subsequent
  lifetimes get new IDs, including across registry instances and target types.

The project still maps gameplay semantics and authored resources. Existing event
duration, amplitude, pitch, clips and time domains remain meaningful. The package
provides default coordination policies, not automatic artistic authoring.

## Reusable sensory policies

| Policy | Behavior | Output adapter's responsibility |
| --- | --- | --- |
| `FeedbackDeadlineCoordinator` | Preserve an isolated authored hold; anchor same-group upgrades to the first event; combine dense stronger requests within a bounded burst; recover according to actual occupancy | Apply the returned absolute deadline through an owned stop/time source |
| `FeedbackSignalCoordinator<TSignal>` + `FeedbackMixing` | Suppress repeated signals; preserve an upgraded group's phase; promote explicit output over fallback; apply additive/replace/strongest rules and amplitude budget | Signal identity/liveness, waveform, direction, output and cancellation |
| `FeedbackPeakCoordinator` | Bound local active groups and pending contributions; pair each hit's base with its own final multiplier; keep the peak without replaying the animation | Animation seed, pose, clock and exact group completion |
| `FeedbackBatchCounter` + `FeedbackAudioLayers` | Count distinct contacts inside an existing batch; distinguish single versus clustered output; calculate bounded pulses, cluster/weight layers, volume and pitch | Clip selection, spatialization, pooled voices and the original batch's flush point |
| `FeedbackGroupGate` | Same-group duplicate/stronger admission with commit after successful output | Existing haptic/device priority, energy budget, pause/focus/disconnect safety |
| `FeedbackReservationCoordinator<TVariant>` + `FeedbackScaleState` | Reserve before nested output; bound per-frame counts; retain per-target/prefab identity; compose profile and coordinated scale once | Spawn/pool, variant meaning, critical exemptions and restoring the actual transform |

`FeedbackDefaults` supplies stop recovery/burst/retention and model capacities.
Override a channel's constructor policy when necessary, rather than putting a
cooldown on every weapon or event. Recovery and capacity budgets remain explicit;
removing a blind cooldown does not remove safety or resource limits.

### Global stop and independent local contacts

```csharp
if (effectiveStopSeconds > 0 &&
    stop.TryRequest(global, effectiveStopSeconds, important, extraRest, now, out var deadline))
    ownedTimeSource.RequestUntil(deadline);

// One counter per existing clip batch, with its original flush deadline.
bool newContact = batch.Observe(contact, resolvedVolume, now, flushAt);
if (newContact) distinctContacts++;
var layers = FeedbackAudioLayers.Evaluate(distinctContacts, peakVolume, basePitch,
    flushAt - startedAt, audioSettings, clusterClipAvailable, weightClipAvailable);
```

The output methods are project/sink APIs. Do not introduce a new audio window,
serialize unrelated effects into a queue, or use contact deduplication to suppress
damage. A zero configured stop creates no request and receives no hidden minimum.
Two-argument `FeedbackKey` construction and the existing ledger/reservation APIs
remain available.

### Signal and model adapters

Implement the small `IFeedbackSignal` interface and equality using exact owned
output identity. `IsPlaying` must reject missing, completed, cancelled or pooled
outputs; `Cancel()` must check that ownership before touching the output. Attempt
actual output, then call `Commit`; a failed sink must not spend the identity.
For upgrades, retain the prior signal's phase/envelope and replace its contribution,
rather than replaying the full amplitude additively.

For a local model, call `FeedbackPeakCoordinator.TryBegin` with a unique nonzero
hit ID, then resolve that hit with its own final multiplier. Render `Strength(group)`;
call `FinalizeUnresolved` before rendering and `End` on natural completion. A
failed newly created output uses `CancelGroup`, not `End`. Reset on target disable
or a new lifecycle. Tokens from old lifetimes cannot retune or cancel fresh groups.

The policies intentionally accept external clocks and do not depend on a specific
Cinemachine, tween, audio or haptics package. This keeps the same coordination code
usable with different Unity versions and output backends. A concrete consumer
only binds its identities, clock, resources, playback and lifecycle hooks.

## Visible-effect reduction

`VisualEffectBudget<TVariant>` reuses reservation coordination for a caller-owned
view-wide budget. Distinct variants share frame, short-window and active-lifetime
limits; equal variants in a time bucket share one visible presentation. `Full`,
`Simplified` and `Minimal` are independent of opacity. Important effects bypass
reduction. Release each accepted tracked lease on pool return or natural end;
reset invalidates stale handles. Clocks, lifecycle, spatial variant keys and
eligibility belong to the consumer. Admission controls appearance, never damage,
gameplay object creation, network authority or simulation.

`VisualEffectSettings.Opacity` sanitizes the user's multiplier and retains full
strength for protected/exempt effects. Unity renderer state is owned by the
separate Render package, not this pure-System assembly. Run
`VisualEffectBudgetTests` for flooding, independent density/opacity, lifecycle,
important output and clock contracts.

## Storage, timing and failure contracts

- The low-level ledger uses preallocated indexed lookup, a bounded expiry heap
  and reusable slots. Duplicate lookups are expected O(1); expiry/retention changes
  cost O(log capacity). No valid ledger operation allocates per request. Indexed
  storage uses more constructor memory than the old linear array; small-capacity
  high-churn traffic can be slower. Measure the relevant workload.
- Reservation variant storage is created on first use and retained for reuse
  across expiry/reset; pending slots are reused. First use of a new target or
  variant may allocate. Warming, retained capacity and per-request GC are separate.
- Retention is a bounded deduplication deadline, not effect duration. Equal/weaker
  copies do not renew it. After expiry a key may play again; producers must not
  reuse permanent IDs indefinitely and expect permanent deduplication.
- Ledger `Evaluate` is a single-use synchronous proposal. Attempt output and
  `Commit` without reentry or clock advance. Reservation APIs support the documented
  nested-output path; complete the exact reservation after the output attempt.
- Full ledger capacity rejects new keys rather than evicting a live identity.
  Reservation capacity fallback, important-request budget exemptions and output
  limits are explicit per-channel rules, not a universal strength score.
- Invalid input and backwards clocks follow each API's contract. `Reset` begins
  a new lifecycle and invalidates old proposals/handles. Actual playback, foreign
  pause tokens and device safety remain with their original owners.

## Verification

Package NUnit tests use synthetic data for single/multiple contacts, anchored
deadlines, output upgrades, nested peak resolution, expiry/cancellation, retained
storage and warmed allocation contracts. The same contracts can run against the
pure-System runtime outside Unity; Unity consumers also need their focused adapter
checks and comparable recordings. Microbenchmarks do not establish Player FPS,
GPU cost or physical haptic feel.

## Continuous combat presentation

`CombatPresentationSettings` resolves one 0..1 value into optional visual burst/lifetime
budgets, numeric merge-window scaling and repeated-flash cadence. One is the existing
full baseline; zero rejects optional effects while text remains controlled by its own
on/off flag. Consumers decide source eligibility and persistent/essential roles.
Use separate burst and lifetime admission when rejecting a cosmetic request before
spawn; `TryAcquireLifetime` does not spend the burst allowance twice.

`FeedbackCadenceLimiter` owns per-target repetition deadlines;
`TargetFeedbackReservations` owns weak target scopes for the existing particle/output
reservation contract. `FeedbackNumberMerger` owns numeric windows and totals, while
`FeedbackUpdateQueue` coalesces display updates without overwriting newer reentrant
versions. Target-lifetime and display-activation predicates are supplied once by the
consumer. Reset a target when its pool activation ends and reset the session at its
own shutdown boundary. A failed output never commits a reservation; old or foreign
tickets cannot affect a newer target/session.

Numeric windows reuse warmed target/style state; style capacity is bounded and
overflow preserves the individual first output. Full/protected cadence paths bypass
target lookup. These types do not spawn objects, choose shaders or call renderers.
The retired `VisualEffectSettings.Opacity` helper remains as an obsolete, inactive
API: density never calls it. Authored local fades belong to the renderer adapter and
remain supported independently. Do not restore the player opacity setting by adding
a second active policy or migration branch.

## Adaptive optional-effect budget

`AdaptiveFeedbackBudget` treats the player density as an immutable ceiling. The
consumer reports optional effect-root starts/stops and one unscaled frame sample;
no renderer enumeration, individual-particle query or GPU profiler is required.
Full, disabled and manually fixed settings retain their normal paths. Under
sustained crowding or frame-time pressure, the controller resolves a lower effective
density through the same visual, numeric-window and flash-cadence curve as the slider.
The player's stored ceiling remains unchanged. Recovery is
slower than reduction. Loading, pause/background state and the selected frame cap
are supplied by the adapter and excluded from overload samples. A single hitch
cannot trigger repeated quality cuts.

A performance-only reduction is a bounded attribution probe: if frame time does
not improve, further performance-driven cuts stop while the optional scene is
sparse. Independent crowding control may still tighten output. Caps have a floor,
root counts cannot become negative, and an epoch change invalidates old root
observations after a room/session/settings reset. Do not write adaptive pressure back to settings.
All hot observation methods and settings resolution are allocation-free after
construction. Frame targets and sampling eligibility remain platform/game policy.

`VisualEffectCoalescer<TKey,TOutput>` keeps a bounded set of live representatives
of equivalent continuous visuals, promoting another live output when a selected one ends.
The default is one. `ConfigureMaximum` changes the limit without invalidating leases;
the caller then reapplies current visibility. `ReplaceOutput` transfers a representative
slot to a new live output without reordering its spatial admission. A consumer can combine
exact-variant coalescing with a regional layer limit, retaining distinct targets/endpoints.
It has bounded preallocated slots, generation-checked leases, expected O(1) lifecycle
operations and warmed allocation-free churn. Capacity overflow preserves the output.
The consumer supplies spatial/style keys and persistent-role eligibility, preserves
essential cues and gameplay objects, and applies visibility only when membership changes.
It may refresh moving-root keys at a low frequency while coalescing is active; no
individual particle processing is needed. Disable/reset restores consumer-owned visibility.
