# ZeroEngine.Network

> **Legacy NGO/UGS wrapper.** This package is retained for existing consumers only.
> New shared multiplayer work should use `com.zerogamestudio.zeroengine.multiplayer`
> and select one networking/runtime adapter. Do not install both packages as two
> competing gameplay networking stacks.

Networking utilities and Netcode for GameObjects wrapper.

## Features

- `ZeroNetworkBehaviour` - Convenience base class
- `ReconnectionHandler` - Automatic reconnection
- NGO/UGS integration (conditional)

## Conditional Defines

- `ZEROENGINE_NETCODE` - Netcode for GameObjects present
- `ZEROENGINE_UGS` - Unity Gaming Services present

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)
