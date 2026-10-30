# ZeroEngine.World

World and environment systems.

## Modules

- **Environment** - Weather and day/night cycle system
- **Calendar** - In-game calendar and time system
- **Minimap** - Minimap rendering and markers
- **Presentation** - Engine-agnostic camera, occupancy, visibility, and cell stability contracts

## Presentation Contracts

- Camera requests carry stable composition, follow/look target, blend profile, scope, source, and priority identifiers. `SourceId` identifies the stable owner; the lease is the unique request lifetime. A P5 lifecycle bridge owns and releases the lease for its scope, while the adapter/profile resolver maps blend identifiers to engine-specific behavior.
- Environment requests arbitrate profile and blend intent by priority with a stable FIFO tie-break. Their coordinator is bound to its construction thread so project adapters fail closed instead of mutating presentation state from a worker thread.
- Interior occupancy requests arbitrate independently per subject and restore the previous winning interior when a lease ends.
- Visibility requests combine the minimum value within each channel and multiply values across channels, allowing independent cutaway and occlusion owners to release precisely.
- Cell presentation stability is an explicit post-readiness contract and does not change world-cell loading or readiness semantics.

Presentation coordinators are not thread-safe and must be called serially by the owning presentation loop. Unity adapters must marshal calls to the main thread. Change-event handlers must not throw or re-enter coordinator mutation.

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Surface wetness and snow

`SurfaceWeatherEvolution` is `zero.environment.surface_weather.v1`: immutable bounded
wetness/snow state, six explicit rates, rain/snow intensity, exposure and clock delta.
Rain wets exposed terrain, snow accumulates, clear weather dries/melts, and rain melting
can add moisture. Covered regions do not accumulate precipitation; they can still dry
or melt existing coverage. Invalid inputs preserve state; delta zero freezes it.

This contains no engine clock, material, physics sampler, camera or weather singleton.
Consumers own region identity, unloaded-time history, thermal/rate policy, serialization
and whether a surface can carry snow. Camera relocation clears transient particles; it
never implicitly resets this independent ground state.
