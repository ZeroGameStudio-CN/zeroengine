# Minimal TCE Graph Sample

This sample demonstrates a project-agnostic Trigger -> Condition -> Effect graph:

1. `OnInstallTriggerData` fires when the runtime is installed.
2. `NumericSourceConditionData` checks a numeric install source.
3. `CooldownConditionData` shows a reusable stateful condition.
4. `DebugLogEffectData` writes through the TCE log hook.

## Smoke Route

1. Import the `MinimalGraph` sample from Package Manager.
2. Add `MinimalTceGraphExample` to an empty GameObject.
3. Enter Play Mode.
4. Confirm the Console receives the configured debug log message.

The sample intentionally avoids project-specific ability, card, equipment, buff, inventory, or stat systems.
