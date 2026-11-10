# ZeroEngine Events

Typed event delivery, scoped subscriptions, bounded queues and event diagnostics.

`ZeroEngine.Events` contains the engine-independent bus. `ZeroEngine.Events.Unity`
provides ScriptableObject event channels. The Editor assembly provides the bus
observatory; the core does not depend on that window or on a project singleton.

Create an `EventBus` for the scope that owns the events. Implement `IGameEvent`,
subscribe with `Subscribe<TEvent>`, and publish through that bus. Retain the returned
subscription token or assign an owner in `EventSubscriptionOptions`, then release
subscriptions with `Unsubscribe` or `UnsubscribeOwner` when that scope ends.

Publishing uses the bus's configured thread policy. `EventBusQueue` provides a
bounded handoff when producers cannot publish on that thread. Inspect the queue
and publish results instead of assuming every subscriber or enqueue succeeded.

Install through the official Git UPM repository, pinned to the same tested
commit as the other selected ZeroEngine packages:

```text
https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.events#<tested-commit>
```

Package tests cover event delivery, subscription lifetime, channels and
observability. Add the package to the consumer's `testables` list and run its
`ZeroEngine.Events.Tests.Editor` assembly in the supported Unity test environment.
