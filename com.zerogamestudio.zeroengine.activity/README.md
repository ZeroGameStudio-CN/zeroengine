# ZeroEngine.Activity

Game-independent Full / Reduced / Dormant activity and per-world budgeted work.
Requires Unity 2022.3 or later. No POB or third-party package dependency.

- `ZeroEngine.Activity` has no Unity engine reference: policy, safe defaults,
  hysteresis, owner-scoped keep-alive handles, decision gating and a work queue.
- `ZeroEngine.Activity.Unity` supplies optional `ActivityWorldDriver`,
  `ActivityObserver` and `ActivityAgent` components. Create a driver explicitly
  for each gameplay world; configure participant clocks and safety callbacks.
- A game adapter decides which work may be reduced and how state is preserved.
  Never use this module to reset GameObjects, treat sleep as death, freeze
  gameplay clocks, or infer that a visually hidden entity cannot interact.

Preload assets asynchronously, then enqueue their synchronous creation under a
stable world-scoped key. Producers in the same world share one queue and budget.
Callbacks must remain bounded; a time budget prevents starting the next callback
but cannot interrupt an expensive one. Eligibility predicates are read-only.
Cancel stale work when its content lifetime ends. Resource loading, pooling,
save state and gameplay outcomes stay with their existing owners.

`ActivityController.Evaluate` accepts monotonic policy time. Decision ticks use
the caller's gameplay clock, not frame count. Missing observers or invalid
observations keep the entity fully active. Dormancy preserves instances and does
not imply memory release. Keep-alive handles must be disposed by their owner.
Adapter failures latch that participant at Full until a successful Configure.
Full-state application is retried on subsequent ticks when necessary, while the
error is reported once and other participants continue. IsFaulted exposes this
safe fallback; callers must not interpret a failed adapter as dormant or safe to
unload. World disable suspends its queue, and world destruction disposes it.

Install through UPM using
`https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.activity#<tested-commit>`.
Never copy this package into a consumer's Assets folder. Run the package's
`ZeroEngine.Activity.Tests.Editor` tests in Unity before pinning a release.
