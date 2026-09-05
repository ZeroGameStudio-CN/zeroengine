# LocalDirect Room UI sample

This sample is intentionally visual-light: it uses `OnGUI` so it can validate a blank room without importing a UI framework.

## Scene setup

1. Install the pinned FishNet dependency listed in the package root README.
2. Create one GameObject and add `NetworkManager`, `Tugboat`, `FishNetIdentityBridge`, `FishNetConnectionDriver`, `MultiplayerBootstrap`, and `LocalRoomSampleController`.
3. Assign a valid `MultiplayerSessionConfig` to the sample controller. The remaining references resolve from the same GameObject.
4. Put the scene in Build Settings and build a Windows player.
5. Open **Window > ZeroEngine > Multiplayer > Local Launcher**, select the player, and choose **Launch Both**.

`MultiplayerBootstrap` wires the room service as the driver's remote authorizer. The host and client logs each emit `ZEROENGINE_M2_READY` after FishNet authentication, LocalDirect identity admission, and the sample synchronization acknowledgement complete.

`LocalDirect` identities are explicit development identities. They are not suitable for release authorization.
