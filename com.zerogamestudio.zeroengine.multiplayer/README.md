# ZeroEngine.Multiplayer

Configurable room and multiplayer-session orchestration for ZeroEngine projects.

The `Core` assembly owns public models, configuration, state transitions, compatibility checks, invite routing, reconnect policy, stable-seat validation, and the session coordinator. It does not reference Steamworks, FishNet, or a game project's runtime types. Optional assemblies provide LocalDirect rooms, FishNet transport control, Steam lobbies, editor validation, and a read-only presentation state.

## Status

Version `0.2.0` contains the M2 implementation:

- `LocalDevelopmentRoomService` and a stable command-line descriptor for two-process development rooms.
- `FishNetConnectionDriver` for Tugboat LocalDirect or FishySteamworks SteamP2P, plus an identity bridge based on FishNet's authenticated connection event.
- the single-owner `SteamRuntimeOwner`, Steam lobby metadata/service, invitations, refresh, leave, and host-loss detection.
- host-owned room-state publishing for phase, joinability, and monotonic session generation, plus a fail-closed remote admission gate.
- Setup Validator, config inspector, local player launcher, and the importable **LocalDirect Room UI** sample.

LocalDirect identities are explicitly untrusted development identities. SteamP2P resolves the remote platform identity from FishySteamworks' authenticated server connection address.

This package is separate from `com.zerogamestudio.zeroengine.network`; it does not replace or migrate that package's NGO/UGS path.

## Optional dependency baseline

The package itself has no hard UPM dependency so `Core`, `Local`, `Presentation`, and editor configuration tooling still compile without a networking SDK. The FishNet and Steam assemblies become active only when their packages are present.

The tested baseline is fixed to:

| Dependency | Version | Pinned source commit |
| --- | --- | --- |
| FishNet | 4.6.15 | `caf94d2cf5bd7cd2531452b5bf68d20c9a8eae1a` (`4.6.15.1` tag) |
| FishySteamworks | 4.1.1 | `21e858249249e2c322365fe9fefbe865f290b0d9` |
| Steamworks.NET | 2024.8.0 | `a2fc889ab2672981ec3e6225d551d86ce6923121` |

Pin normalized package mirrors built from these commits; do not track a moving branch. The upstream FishNet tag declares the non-UPM version `4.6.15.1` and includes duplicate quaternion-compression sources on Windows, while FishySteamworks is a repository-root package whose tagged manifest still declares `4.1.0`. Consequently, the two upstream repositories cannot be installed with the previously suggested direct `?path=` URLs. A tested internal mirror or vendored package must normalize FishNet to `4.6.15`, remove the duplicate compression sources, and normalize FishySteamworks to `4.1.1` without changing runtime code.

```json
"com.firstgeargames.fishnet": "<normalized-fishnet-upm-url>#<tested-mirror-commit>",
"com.firstgeargames.fishysteamworks": "<normalized-fishysteamworks-upm-url>#<tested-mirror-commit>",
"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=com.rlabrecque.steamworks.net#a2fc889ab2672981ec3e6225d551d86ce6923121"
```

The source commits in the table remain the provenance pins; the mirror commits record only packaging normalization. Do not use LLS's Unity-6-modified FishNet copy as the Unity 2022 source, and do not retain Asset-folder copies with the same assembly names when installing these UPM packages.

## Core flow

1. Construct `MultiplayerSessionCoordinator` with one `MultiplayerSessionConfig`, an `IPlatformRoomService`, an `INetworkConnectionDriver`, and an `IMultiplayerGameAdapter`.
2. Initialize, create or join, and consume `MultiplayerSessionSnapshot` or `MultiplayerViewState` from project UI.
3. A joining client remains in `Synchronizing` after local preparation. The authoritative game/network bridge must call `ConfirmLocalSynchronization` with the current session ID and generation before the client becomes `Ready` or returns to `InGame`.
4. Server-side peer synchronization and restoration use `SynchronizePeerAsync` and `RestorePeerAsync`; the package never reads game-specific state.

Host room services may implement `IRoomStatePublisher`; the coordinator then publishes `InRoom`, `Ready`, `Starting`, rollback, and `InGame` state without exposing platform APIs to UI or game code. A room service used by `MultiplayerBootstrap` as a FishNet host must implement `IRemoteConnectionAuthorizer`. The bootstrap wires this automatically: Steam re-reads current Lobby metadata and membership before admitting an authenticated FishySteamworks identity, while LocalDirect accepts only the explicitly configured development identity.

`MultiplayerSessionConfig` exposes room sizing, operation timeouts, compatibility metadata, transport defaults, logging, and reconnect limits. Its default reconnect schedule is three attempts with 0.5/1.5/3 second delays, four seconds per attempt, an 18 second client deadline, and a 20 second server seat grace period.

## Steam ownership

One `SteamRuntimeOwner` is the only component allowed to call `SteamAPI.Init`, `SteamAPI.RunCallbacks`, and `SteamAPI.Shutdown`. FishySteamworks only initializes relay access and remains subordinate to that runtime. Projects with an existing Steam manager must remove it or provide an explicit replacement runtime before enabling this path.

Run **Window > ZeroEngine > Multiplayer > Validate Setup** before testing. For a LocalDirect scene it checks the session config, NetworkManager, driver, Tugboat, and Build Settings. For Steam it additionally checks the unique runtime owner, FishySteamworks, legacy owners, and lifecycle call sites.

## LocalDirect sample and launcher

Import **LocalDirect Room UI** from Package Manager, follow its README to create a blank FishNet scene, and build a player. Then open **Window > ZeroEngine > Multiplayer > Local Launcher**, select the executable, and launch Host, Client, or both. Each process receives an explicit room/session/compatibility descriptor and a unique log path. A successful blank synchronization emits `ZEROENGINE_M2_READY` in both logs.

Real Steam remains a manual two-account test: compile/setup validation and LocalDirect dual-process testing do not prove overlay, friend invitation, relay connectivity, or account entitlement.

## Temporary local development

During package development only, add this package to a consumer project's `Packages/manifest.json` with a `file:` URL and add `com.zerogamestudio.zeroengine.multiplayer` to `testables` when package tests need to run.

Shared and production branches must instead use a tested commit:

```json
"com.zerogamestudio.zeroengine.multiplayer": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.multiplayer#<tested-commit>"
```
