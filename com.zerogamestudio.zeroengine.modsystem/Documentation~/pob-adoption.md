# POB Adoption Notes

POB should consume `ZeroEngine.ModSystem` for:

- `ModPathResolver`
- `IModSource`
- `IAsyncModSource`
- `ModSourceRegistry`
- `ModSourceQueryResult`
- `ModLoadOrchestrator.LoadFromSourcesAsync`

POB should keep these in `POB.Workshop`:

- `mod.json` player-facing schema
- `cards`, `simpleCards`, `weapons`, `projectiles`, and `quests`
- `SimpleCardRecipeCompiler`
- `ModCardFactory`, `ModWeaponFactory`, `ModProjectileFactory`, and `ModQuestFactory`
- POB text table registration
- POB config injection

POB must not create a separate `ZeroEngine.Workshop` package for these shared primitives.

POB startup must await source discovery before importing or projecting save data. Steam
sources should implement `IAsyncModSource` over their real UGC completion callback. The
legacy callback/synchronous load path is compatibility-only and must not be used for a
release startup chain.
