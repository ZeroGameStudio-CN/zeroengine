# ZeroEngine.Gameplay

General gameplay systems.

## Modules

- **Interaction** - Interactable objects with conditions
- **Tutorial** - Tutorial sequence system
- **Command** - Command pattern with undo/redo

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support
- `com.unity.inputsystem` - Interaction and tutorial input support
- `com.unity.textmeshpro` - Interaction prompt UI text support

## Config Validation

`ZeroEngine.Gameplay.Editor` provides `GameplayConfigValidator` for Editor tests and release checks:

- `InteractionConfigSO` detection ranges, input action names, prompt prefab assignment, timing, and hint templates.
- `TutorialConfigSO` UI timing, typewriter speed, highlight/arrow settings, audio volume, key conflicts, and target search retries.
- `TutorialSequenceSO`, `TutorialStepSO`, `TutorialSO`, and `TutorialGroupSO` IDs, display text, duplicate references, prerequisites, rewards, conditions, highlights, actions, and polymorphic tutorial step parameters.

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
