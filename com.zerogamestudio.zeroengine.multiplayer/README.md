# ZeroEngine.Multiplayer

Configurable room and multiplayer-session orchestration for ZeroEngine projects.

The `Core` assembly owns public models, configuration, state transitions, compatibility checks, invite routing, reconnect policy, stable-seat validation, and the session coordinator. It does not reference Steamworks, FishNet, or a game project's runtime types. Optional assemblies provide LocalDirect rooms, FishNet transport control, Steam lobbies, editor validation, and a read-only presentation state.

## Status

Version `0.2.0` contains the shared room/session implementation used by the
GalleryKeeper M4 integration:

- `LocalDevelopmentRoomService` and a stable command-line descriptor for two-process development rooms.
- `FishNetConnectionDriver` for Tugboat LocalDirect or FishySteamworks SteamP2P, including selected-route control behind one FishNet `Multipass`, plus an identity bridge based on FishNet's authenticated connection event.
- the single-owner `SteamRuntimeOwner`, Steam lobby metadata/service, invitations, refresh, leave, and host-loss detection.
- host-owned room-state publishing for phase, joinability, and monotonic session generation, plus a fail-closed remote admission gate.
- pre-transport game preparation, post-connect local synchronization, server-side peer restore, and automatic client reconnect with configured retry/deadline limits.
- an authenticated game-channel start confirmation for development room services whose platform descriptor cannot receive cross-process metadata updates.
- Setup Validator, config inspector, local player launcher, and the importable **LocalDirect Room UI** sample.

LocalDirect identities are explicitly untrusted development identities. SteamP2P resolves the remote platform identity from FishySteamworks' authenticated server connection address.

This package is separate from `com.zerogamestudio.zeroengine.network`; it does not replace or migrate that package's NGO/UGS path.

## Optional dependency baseline

The package itself has no hard UPM dependency so `Core`, `Local`, `Presentation`, and editor configuration tooling still compile without a networking SDK. The FishNet and Steam assemblies become active only when their packages are present.

The tested baseline is fixed to:

| Dependency | Version | Pinned source commit |
| --- | --- | --- |
| FishNet | 4.7.2 | `de19b5d66459f60400ffd0edc443c4da173a01e7` |
| FishySteamworks | 4.1.1 | `21e858249249e2c322365fe9fefbe865f290b0d9` |
| Steamworks.NET | 2024.8.0 | `a2fc889ab2672981ec3e6225d551d86ce6923121` |

Pin the FishNet and Steamworks.NET source commits; do not track a moving branch. FishNet `4.7.2` is a valid package at `Assets/FishNet` and avoids the invalid four-part manifest version and duplicate compression sources found in the earlier `4.6.15.1` candidate. FishySteamworks `4.1.1` must be imported from the upstream release asset (SHA-256 `5698D16BD29B8B08D35E12A9B817CE69992F70D7C14B64810961691ECD9AFC57`): its Git root package omits the transport source, while its nested package has no asmdef and is therefore not compiled from `Packages/`.

```json
"com.firstgeargames.fishnet": "https://github.com/FirstGearGames/FishNet.git?path=/Assets/FishNet#de19b5d66459f60400ffd0edc443c4da173a01e7",
"com.rlabrecque.steamworks.net": "https://github.com/rlabrecque/Steamworks.NET.git?path=com.rlabrecque.steamworks.net#a2fc889ab2672981ec3e6225d551d86ce6923121"
```

This exact dependency set passed all 59 package EditMode tests in a fresh Unity 2022.3.62f3 project. Do not use LLS's Unity-6-modified FishNet copy as the Unity 2022 source, and do not retain Asset-folder copies with the same assembly names when installing these UPM packages.

## Core flow

1. Construct `MultiplayerSessionCoordinator` with one `MultiplayerSessionConfig`, an `IPlatformRoomService`, an `INetworkConnectionDriver`, and an `IMultiplayerGameAdapter`.
2. Initialize, create or join, and consume `MultiplayerSessionSnapshot` or `MultiplayerViewState` from project UI.
3. `PrepareSessionAsync` runs before the selected transport starts, so the project can load and register its network scene without racing the first connection. `SynchronizeLocalAsync` runs after the connection is established and must wait for the authoritative local snapshot before the client becomes `Ready` or returns to `InGame`.
4. A client may learn that the host started through platform room metadata. A project with a static development descriptor instead calls `ConfirmRemoteSessionStarted` from its authenticated server-to-client game message; the call is generation-checked and idempotent.
5. Server-side peer synchronization and restoration use `SynchronizePeerAsync` and `RestorePeerAsync`. An unexpected in-game client disconnect automatically refreshes the room, reconnects the same host/session/generation, and waits for `SynchronizeLocalAsync` before restoring `InGame`.
6. The package never reads game-specific state.

Host room services may implement `IRoomStatePublisher`; the coordinator then publishes `InRoom`, `Ready`, `Starting`, rollback, and `InGame` state without exposing platform APIs to UI or game code. A room service used by `MultiplayerBootstrap` as a FishNet host must implement `IRemoteConnectionAuthorizer`. The bootstrap wires this automatically: Steam re-reads current Lobby metadata and membership before admitting an authenticated FishySteamworks identity, while LocalDirect accepts only the explicitly configured development identity.

`MultiplayerSessionConfig` exposes room sizing, operation timeouts, compatibility metadata, transport defaults, logging, and reconnect limits. Its default reconnect schedule is three attempts with 0.5/1.5/3 second delays, four seconds per attempt, an 18 second client deadline, and a 20 second server seat grace period.

## Steam ownership

One `SteamRuntimeOwner` is the only component allowed to call `SteamAPI.Init`, `SteamAPI.RunCallbacks`, and `SteamAPI.Shutdown`. FishySteamworks only initializes relay access and remains subordinate to that runtime. Projects with an existing Steam manager must remove it or provide an explicit replacement runtime before enabling this path.

Run **Window > ZeroEngine > Multiplayer > Validate Setup** before testing. For a LocalDirect scene it checks the session config, NetworkManager, driver, Tugboat, and Build Settings. For Steam it additionally checks the unique runtime owner, FishySteamworks, legacy owners, and lifecycle call sites.

## LocalDirect sample and launcher

Import **LocalDirect Room UI** from Package Manager, follow its README to create a blank FishNet scene, and build a player. Then open **Window > ZeroEngine > Multiplayer > Local Launcher**, select the executable, and launch Host, Client, or both. Each process receives an explicit room/session/compatibility descriptor and a unique log path. A successful blank synchronization emits `ZEROENGINE_M2_READY` in both logs.

Real Steam remains a manual two-account test: compile/setup validation and LocalDirect dual-process testing do not prove overlay, friend invitation, relay connectivity, or account entitlement.

The GalleryKeeper Unity 6000.3.10f1 integration currently runs 67 package EditMode tests inside its 110-test integrated EditMode suite. Its Development Player dual-process smoke covers `Ready`, authoritative start, an unexpected client disconnect, reconnect, restored snapshot, full puzzle completion, and leave. This does not replace the fresh Unity 2022 dependency-baseline result above or the real Steam matrix.

## Temporary local development

During package development only, add this package to a consumer project's `Packages/manifest.json` with a `file:` URL and add `com.zerogamestudio.zeroengine.multiplayer` to `testables` when package tests need to run.

Shared and production branches must instead use a tested commit:

```json
"com.zerogamestudio.zeroengine.multiplayer": "https://github.com/liuzqk/zeroengine.git?path=com.zerogamestudio.zeroengine.multiplayer#<tested-commit>"
```
