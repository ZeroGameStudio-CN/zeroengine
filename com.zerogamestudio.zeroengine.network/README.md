# ZeroEngine.Network

Networking utilities and Netcode for GameObjects wrapper.

## Features

- `ZeroNetworkBehaviour` - Convenience base class
- `ReconnectionHandler` - Automatic reconnection
- NGO/UGS integration (conditional)

## Designer Config Validation

`ZeroEngine.Network.Editor` provides `NetworkConfigValidator` for server configs.
It reports missing local IPs, invalid ports, non-positive player caps, and
invalid frame-rate settings before a server build is made.

## Conditional Defines

- `ZEROENGINE_NETCODE` - Netcode for GameObjects present
- `ZEROENGINE_UGS` - Unity Gaming Services present

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.unity.netcode.gameobjects` (1.15.1) - Netcode runtime for Unity 2022.3
- `com.unity.transport` (2.7.3) - Network transport assembly
- `com.unity.services.core` (1.18.0) - Unity Gaming Services initialization
- `com.unity.services.authentication` (3.7.1) - UGS authentication
- `com.unity.services.lobby` (1.3.0) - Lobby assembly (`Unity.Services.Lobbies`)
- `com.unity.services.relay` (1.2.0) - Relay allocation and join support

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
