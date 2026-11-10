# ZeroEngine Game Feel

Small, optional, pure-System coordination for feedback that shares an attack/pulse
identity. Each channel and scope owns a separate `FeedbackCoordinator`; the package
does not choose hit-stop time, camera amplitude, audio voices, haptic energy, or a
universal priority score. Existing channel mixers and safety budgets keep ownership
of their output.

This is unrelated to `ZeroEngine.Feedback`, the player issue-reporting package.
There are no package dependencies and the runtime assembly has
`noEngineReferences: true`.

## Installation

Use the official UPM repository pinned to the commit validated with your consumer:

```json
"com.zerogamestudio.zeroengine.gamefeel": "https://github.com/ZeroGameStudio-CN/zeroengine.git?path=/com.zerogamestudio.zeroengine.gamefeel#<tested-commit>"
```

Replace `<tested-commit>` with an actual verified commit. `file:` pins are temporary
local integration tools, not a production installation or release. Add
`ZeroEngine.GameFeel` to the consuming assembly's references. To discover package
NUnit tests in a Unity test project, include this package in the project's
`testables` array. Tests live in `ZeroEngine.GameFeel.Tests.Editor`.

## Contract

- `FeedbackKey(long attackId, long pulseId)` is immutable; attack IDs must be
  positive and pulse IDs nonnegative. Applications generate stable identities and
  choose pulse meaning. A scope (camera, target lifecycle, owner) belongs to its
  coordinator instance, not an ambient global registry.
- `Evaluate(key, strength, now)` accepts a new key or a strictly greater strength.
  `strength` is a finite nonnegative comparison value in that channel's units.
  Equal/weaker requests are duplicates. The package does not interpret zero as
  disabled; applications retain their own configuration semantics before submitting.
- `now` is a finite, nonnegative, monotonically increasing external clock; equal
  timestamps are supported. The package does not query Unity time, multiply time
  scales, or add transitions. Invalid input or a backwards clock changes no state.
- Evaluate is not a reservation. Attempt the actual output, then
  `Commit(evaluation, retainUntil, out handle)` only if it accepted. The exact
  proposal is single-use. Any intervening commit, cancellation, expiry, reset, or
  clock advance invalidates it. Calls are synchronous, single-threaded and must
  not reenter the coordinator during the output call. Do not hold evaluations
  across frames, async work, or callbacks; stale decisions must be reevaluated
  before a later output attempt.
- `retainUntil` is an absolute, finite deadline strictly after the evaluated time.
  It is a bounded **deduplication window**, not an effect duration or a universal
  TTL. Applications choose a window appropriate to their identities and lifetime.
  Stronger commits preserve `FirstAcceptedAt` and never shorten an existing
  retention deadline. Equal/weaker evaluations never refresh it. At the deadline
  the key may be accepted again, so an application must not rely on this cache to
  deduplicate an arbitrarily late collision from a permanent attack identity.
- Storage is one fixed array allocated in the constructor. Operations scan at
  most `Capacity` entries and make no per-request allocation. Expiry runs during
  valid evaluations. `Count` describes the state after that most recent operation,
  not a clock that advances on its own. Full capacity rejects a new identity even
  if stronger; it never evicts a live key and enables its accidental replay.
- `Reset()` begins a new lifecycle, clears the ledger and accepts a new clock
  origin. `Cancel(handle)` removes only that exact committed version. Foreign,
  pre-reset, replaced, or reused-slot handles cannot cancel current state. Actual
  effects and pause/time-source tokens remain consumer-owned. Do not cancel a
  ledger entry merely because its short effect ended if duplicate hits must stay
  suppressed for the rest of the configured retention window.

`FeedbackEvaluation` exposes `Decision`, `CanPlay`, `Key`, `Strength`,
`FirstAcceptedAt`, and `PreviousStrength`. Decisions are `New`, `Stronger`,
`Duplicate`, `CapacityExceeded`, `InvalidKey`, `InvalidStrength`, `InvalidTime`, and
`ClockMovedBackwards`. Commit reports `Committed`, `NotAccepted`,
`WrongCoordinator`, `StaleEvaluation`, or `InvalidRetention`. A default evaluation
cannot play or commit, despite the enum's default value.

## Reservation consumer

`FeedbackReservationCoordinator<TVariant>` builds on the identity ledger for
outputs that can re-enter synchronously or need a bounded per-frame budget. The
consumer supplies a small value-type variant such as a target, effect signature,
voice family, or view scope. `TryBegin` performs identity/variant admission;
`Complete` is called immediately after the consumer attempts its output.

The reservation layer owns only coordination state:

- equal or weaker same-variant identity requests are duplicates;
- stronger requests are admitted as upgrades while the consumer decides how to
  replace or blend the live output;
- a finite frame budget can suppress decorative requests while `important`
  requests bypass that budget;
- output failure does not spend the identity reservation;
- bounded pending slots reject recursive re-entry instead of allocating an
  unbounded queue;
- full variant or identity history can fall back to an untracked output so a
  new target is not silently hidden, while existing tracked identities remain
  protected;
- `Reset` invalidates every old reservation and handle for a scope lifecycle.

The package still does not choose a universal cooldown, priority score, output
unit, Unity object, audio voice, camera waveform, haptic energy rule, or visual
quality policy. Each channel creates its own coordinator and maps the returned
decision to its output owner. `CapacityFallback` is an explicit signal for
diagnostics; it is not permission to evict a live identity.

## Hit-stop consumer

The following is integration pseudocode: the policy and output methods belong to
the game, not this package. Resolve authoring/player multipliers once before this
point. A zero configured duration creates no hit-stop request.

```csharp
if (effectiveSeconds <= 0) return; // Preserve this channel's explicit zero rule.
var proposal = hitStopCoordinator.Evaluate(key, effectiveSeconds, realtimeNow);
if (!proposal.CanPlay) return;

// A 20 ms request at t=0 upgraded to 80 ms at t=40 ms ends at t=80 ms,
// not t=120 ms. An upgrade whose original deadline passed creates no new stop.
double endsAt = proposal.FirstAcceptedAt + effectiveSeconds;
if (endsAt <= realtimeNow) return;
double retainUntil = Math.Max(endsAt,
    proposal.FirstAcceptedAt + configuredGroupRetentionSeconds);

// Game-owned policy: distinct-group strong hits may bypass a light-hit cooldown,
// but continuous hold/recovery budgets still bound repeated distinct attacks.
if (!hitStopPolicy.CanAdmit(proposal, realtimeNow, endsAt)) return;
if (!ownedTimeSource.TryApplyAbsoluteDeadline(endsAt)) return;
var result = hitStopCoordinator.Commit(proposal, retainUntil, out var handle);
// Record result; synchronous, nonreentrant output keeps this proposal current.
```

Same-key suppression alone does **not** bound continuous hit-stop from infinitely
many distinct attack/pulse IDs. A consumer must apply its explicit hold/recovery
policy before output, separately from the retention cache. Resetting this ledger
must never reset another owner's pause or slow-motion token.

## Camera consumer

Use another coordinator for the current view. The camera's existing envelope,
direction, stack mode, amplitude cap, and player settings remain authoritative.
The output API below is also application pseudocode.

```csharp
var proposal = cameraCoordinator.Evaluate(key, resolvedAmplitude, realtimeNow);
if (!proposal.CanPlay) return;
double retainUntil = proposal.FirstAcceptedAt + configuredCameraGroupWindow;
if (retainUntil <= realtimeNow) return;

// Replace this group's contribution (or apply only its positive increment).
// Do not replay the full upgraded amplitude as a second additive contribution.
if (!cameraMixer.TrySetGroupContribution(key, resolvedAmplitude, cameraConfig))
    return; // Rejected by mixer: do not remember this as a played camera effect.

var result = cameraCoordinator.Commit(proposal, retainUntil, out var handle);
```

The same attack may therefore have a rejected camera effect and an accepted local
model reaction, or vice versa. No cross-channel cooldown is introduced. Identical
keys can be used in separate target coordinators, provided each target resets its
own coordinator on disable/pool lifecycle changes.

## Verification

`FeedbackCoordinatorTests` uses synthetic times for first-event anchoring,
duplicate/stronger admission, channel separation, retention/overflow, full-width
identity, invalid numbers, stale proposals, reset, and generation-safe cancellation.
`FeedbackReservationCoordinatorTests` additionally covers per-frame budgets,
important bypass, variant/identity capacity fallback, synchronous re-entry,
output failure retry, lifecycle reset, full-width values, and the warm duplicate
allocation path.
These tests prove the coordination contract; they do not establish visual quality,
gameplay responsiveness, or real-device haptic feel. Consumers still need their
targeted integration tests and comparable real-time recordings.
