# ZeroEngine.Narrative

Narrative systems for story-driven games.

## Modules

- **Dialog** - Node-based dialogue system with branching and conditions
- **Quest** - Quest management with objectives and rewards
- **Achievement** - Achievement tracking with optional Steam adapter code

## Editor Tooling

- `ZeroEngine.Narrative.Editor` contains Quest authoring validation and dropdown
  helpers.
- `QuestConfigValidator` reports duplicate quest IDs, missing display names,
  invalid/empty conditions, invalid accept requirements, and invalid rewards
  before quests reach runtime.

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support
- `com.zerogamestudio.zeroengine.data` - Quest, reward, and stat data integration
- `com.zerogamestudio.zeroengine.economy` - Reward and item integration
- `com.zerogamestudio.zeroengine.gameplay` - Gameplay condition integration

XNode and Steamworks adapter code is disabled in the default runtime assembly
until those integrations are moved to optional adapter assemblies or supplied by
project code.

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
