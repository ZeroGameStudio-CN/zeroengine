# Interaction Authority Playground

Import this sample and the optional interaction-multiplayer package's pinned
sibling dependencies, then open `NetworkAuthorityPlayground.unity`.
The sample needs uGUI, the UI module and a compatible EventSystem input module.
It defaults to Offline. Its own background-execution setting is restored when
the last sample view stops. It does not load POB, game saves or a platform account.

Offline, Host and Join use the same authority/core and business providers. Host
listens only on local loopback; open another sample process and Join its displayed
port. Each viewer has its own actor, wallet and input session. The client builds
only a projection; every cost, gift, pickup and purchase changes on the authority.
The sample transport carries bounded frames on worker threads, while gameplay
runs only in the caller's Pump/Unity Update. It is an example transport, not a
replacement for the game's existing FishNet/session stack.

Approach a door, NPC, shared pickup, personal pickup or shop. Interact supports
press/hold/release. Gifts cost one coin; the shop reserves two coins before
confirmation. Cancel or leave range before confirmation to release the reservation.
A late cancellation after success never refunds a completed purchase. The
personal pickup belongs to the host actor; the shared pickup has one winner.
The Approach control is sample navigation to server-known slots, not a production
API for clients to set authoritative positions.

The same shared model runs without Unity as genuinely independent processes.
The .NET 10 tool has no NuGet package dependencies:

```text
dotnet build com.zerogamestudio.zeroengine.interaction.multiplayer/Tools~/AuthorityDemo/AuthorityDemo.csproj -o <external-bin>
python -B com.zerogamestudio.zeroengine.interaction.multiplayer/Tools~/AuthorityDemo/verify_peers.py --dll <external-bin>/AuthorityDemo.dll --output <new-external-evidence-directory>
```

The driver checks separate host/client PIDs, both observation directions, competing
pickup order, personal ownership, command replay, confirmation/cancel ordering,
reordered delayed receipts, server range, pooled generations, spoofed actor/epoch
headers and disconnect cleanup. Each process must exit normally with its transport
workers stopped, and the listener must be closed. The emitted JSONL and summary
are evidence for this sample; POB still needs its actual FishNet/protocol, NPC,
payment, collider/pooling and single-player consumer regressions.
