# Changelog

## [0.3.0] - Unreleased

- Isolate `MultiplayerSessionConfig` in `ZeroEngine.Multiplayer.Configuration` and preserve existing serialized assets with `MovedFrom` metadata.
- Make `ZeroEngine.Multiplayer.Core`, `.Local`, and `.Presentation` free of Unity engine references.
- Add `IMultiplayerSessionSettings` so session orchestration and presentation rules can run with plain .NET configuration objects.
- Reject invalid room-visibility and build-match enum values supplied by custom settings implementations.
- Add permanent package-boundary tests for consumer-project, Unity, FishNet, Steamworks, NGO, and UGS dependency leaks.
- Keep FishNet and Steamworks integrations version-gated and optional at package install time.
- Do not report host room creation complete until the host client has authenticated and completed local synchronization.
- Preserve remote FishNet authentication callbacks that arrive while the host's local client identity is still starting, then synchronize those peers after host room creation reaches a stable phase.
- Keep failed transport/platform cleanup visible and retryable instead of returning to an apparently clean idle state.
- Validate 75/75 EditMode tests with the pinned FishNet/Steamworks dependencies and 71/71 applicable tests in a networking-SDK-free Unity 2022.3.62f3 project.

## [0.2.0] - Unreleased

- Add LocalDirect room descriptors, command-line parsing, and connection-driven member snapshots.
- Add optional FishNet 4.6.15 connection driver, authenticated identity bridge, and fail-closed remote admission gate for Tugboat and FishySteamworks.
- Add optional Steamworks.NET 2024.8.0 runtime ownership, lobby metadata, room lifecycle, invitation, host-loss handling, cancellation-safe native operations, and live Lobby membership authorization.
- Publish host phase, joinability, and monotonic session-generation changes through platform-neutral room-state contracts.
- Add setup validation, configuration inspector, local two-process launcher, and LocalDirect Room UI sample.

## [0.1.0]

- Add platform-neutral room, connection, and game-adapter contracts.
- Add configurable session, compatibility, invite, reconnect, and stable-seat rules.
- Add a serialized multiplayer session coordinator and read-only presentation state.
- Add fake-backed EditMode coverage for the M1 core package.
