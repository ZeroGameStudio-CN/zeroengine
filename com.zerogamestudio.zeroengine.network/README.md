# ZeroEngine.Network

Networking utilities and the existing Netcode for GameObjects/UGS backend.
Keep this package when that concrete SDK integration is needed. The separate
`com.zerogamestudio.zeroengine.multiplayer` package owns SDK-neutral session
orchestration and optional adapters; installing it does not implicitly install
this NGO/UGS implementation.

## Features

- `ZeroNetworkBehaviour` - Convenience base class
- `ReconnectionHandler` - Automatic reconnection
- NGO/UGS integration with an explicit dependency closure

## Conditional Defines

- `ZEROENGINE_NETCODE` - Netcode for GameObjects present
- `ZEROENGINE_UGS` - Unity Gaming Services present

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.unity.netcode.gameobjects` 1.8.0 and `com.unity.transport` 1.4.0
- `com.unity.services.core` 1.16.0 and Authentication 3.5.2
- `com.unity.services.lobby` 1.2.2 and Relay 1.1.1

The assembly references these SDK assemblies directly, so their installation is
required even when a particular service is not used by a scene. Package 2.0.1
declares that closure instead of relying on a consumer to happen to contain it.
The existing scripting defines select code paths; they do not make an undeclared
assembly reference optional.

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)
