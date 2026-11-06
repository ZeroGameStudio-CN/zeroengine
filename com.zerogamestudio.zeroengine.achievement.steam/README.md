# ZeroEngine.Achievement.Steam

Optional desktop provider for projects using the existing Steamworks.NET client SDK.
Install and initialize Steamworks.NET in the host first. The SDK assembly must be named
`com.rlabrecque.steamworks.net`. The adapter compiles when UPM sees that package, or when
the host uses that assembly under Assets and explicitly defines `STEAMWORKS_NET`.
Reference `ZeroEngine.Achievement.Steam` from the desktop composition assembly.

```csharp
using ZeroEngine.Achievement.Providers;

manager.RegisterProvider(new SteamAchievementProvider());
```

The provider retains the legacy direct client calls. It does not implement an
account outbox, server confirmation policy, or Steam reset. A game with its own Steam
synchronization layer, such as POB, keeps that layer as the sole provider.
