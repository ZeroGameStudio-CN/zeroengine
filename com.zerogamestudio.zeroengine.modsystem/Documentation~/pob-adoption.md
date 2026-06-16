# POB Adoption Notes

POB should consume `ZeroEngine.ModSystem` for:

- `ModPathResolver`
- `IModSource`
- `ModSourceRegistry`
- `ModSourceQueryResult`

POB should keep these in `POB.Workshop`:

- `mod.json` player-facing schema
- `cards`, `simpleCards`, `weapons`, `projectiles`, and `quests`
- `SimpleCardRecipeCompiler`
- `ModCardFactory`, `ModWeaponFactory`, `ModProjectileFactory`, and `ModQuestFactory`
- POB text table registration
- POB config injection

POB must not create a separate `ZeroEngine.Workshop` package for these shared primitives.
