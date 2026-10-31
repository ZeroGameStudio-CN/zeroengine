# ZeroEngine.Fishing

Caller-clocked 2D / 2.5D fish movement, small-school steering, bait contact gates and continuous fish resistance. Runtime uses only `System` and `System.Numerics`; no Unity objects, global time, input, camera, UI, items, saves, networking or QTE dependency.

## Scope

- `FishMotion`: ordinary/reserved cruise, delayed attraction, accelerated approach, braking, stable long-mouth alignment, return and caller-guided catch presentation. Positions use XY for the swim plane; Z is an optional caller-owned depth channel.
- `FishPerception`: caller-supplied underwater/visibility/occlusion/noise and opaque lure tags drive interest. Explicit species threat roles add local avoidance without changing stock or rewards.
- `SchoolSteering`: computes separation/alignment/cohesion from a stable snapshot. `SelectCandidates` preserves the host-selected winner and invites nearby fish with a large-body cap.
- `BaitEncounter`: airborne → approaching → inspection → real mouth contact/nibble → ready-to-hook. A slow neighbour cannot hold a reachable encounter forever; an unreachable hook times out without granting a catch.
- `FishFight`: continuous warned bursts, fatigue/recovery, smoothed pull, line load and overload. The host chooses this continuous result authority or an optional higher-level QTE; do not grant two outcomes for one fish.

This package contains our implementation. Reference-game metadata/control-flow research remains outside the package. No reference-game code, sprites, animation, audio, data assets or tuned values are shipped.

## Host responsibilities

Keep species/food IDs, rarity rewards and stock selection in the game adapter. Supply fish bounds and corrected mouth anchors, an external gameplay delta, water-landing/contact observations, reel/slack input and the direction to the rod. Render snapshots and apply impulses through the host's own physics, line and animation systems. Pauses call with zero delta; cancellation resets owned state. Read snapshots without advancing a second clock.

Keep fish components local to the pond lifetime. Clear reservations and contacts on room exit/pool return. No runtime singleton or static mutable state exists.

## Consume after publication

Use the official Git UPM source, pinned to the tested commit. `file:` is only for temporary local verification; no local-path production reference is supplied by this candidate. The package currently has no other ZeroEngine package dependencies. Existing `ZeroEngine.Qte` integrations can remain in a host adapter.

## Verification

`Tests/Editor/FishingCoreTests.cs` contains synthetic behavior regressions. Add the package ID to `testables` only in the verification project's manifest. It uses the existing Unity Test Framework.

For the same pure-core cases without an Editor:

```powershell
pwsh -NoProfile -File ./Tools~/Test-FishingCore.ps1
```

The portable runner uses .NET 10 and NUnit 3; it is not a claim of Unity import, PlayMode, game feel or player-device performance. Unity/package and consumer-adapter results are recorded separately.
