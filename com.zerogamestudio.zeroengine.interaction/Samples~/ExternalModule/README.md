# Independent Module Bridge

`Independent/` is a separate assembly that references only the standard library.
It owns its own small deferred operation and lifetime. `Bridge/` adapts that
operation to `IInteractionExecution`. Importing this sample registers nothing
globally and changes no production scene or project settings.

The example supplies no NPC system, activity catalog, gameplay, UI, rewards or
save format. Those remain independently authored content/project modules. A real
adapter supplies the target's current permission/scope checks and its atomic
business transaction. Old callbacks capture their original execution handle.

The same adapter is an ordinary `IInteractionTarget`: register it with a direct
agent or the existing `InteractionAuthority` / optional replication binding.
Remote inputs still go through the authenticated receiving peer. Only trusted
authority-side content may call the completion port; do not deserialize an
execution handle or trust a client-reported result as an authoritative outcome.

The console example builds the independent module as its own DLL and checks its
assembly references. It runs deferred success, duplicate completion, cancellation
with a late callback, revoked admission, explicit content denial and replacement
activation scenarios using the same runtime core. No NuGet dependency is needed:

```text
dotnet build com.zerogamestudio.zeroengine.interaction/Tools~/ExternalModuleExample/ExternalModuleExample.csproj -o <external-bin>
dotnet <external-bin>/ExternalModuleExample.dll
```

This demonstrates the integration seam. It does not establish POB NPC host,
cross-scene gameplay, save migration or multiplayer activity content compatibility.
