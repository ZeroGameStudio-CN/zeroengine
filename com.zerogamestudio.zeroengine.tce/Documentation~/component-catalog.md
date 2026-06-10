# ZeroEngine TCE Component Catalog

## Trigger

### On Install

- Component ID: `zeroengine.tce.trigger.on_install`
- Data type: `ZeroEngine.TCE.OnInstallTriggerData`
- Runtime type: `ZeroEngine.TCE.OnInstallTrigger`
- Summary: Fires once when the graph is installed.
- Description: Use this trigger for immediate setup rules that should run after all conditions and effects are initialized.
- Fields:
  - `Order` (`System.Int32`, default `0`): Trigger ordering value used by authoring tools and docs.

## Condition

### Chance

- Component ID: `zeroengine.tce.condition.chance`
- Data type: `ZeroEngine.TCE.ChanceConditionData`
- Runtime type: `ZeroEngine.TCE.ChanceCondition`
- Summary: Passes based on a deterministic random source.
- Description: Use this condition when a trigger, owner, or install source exposes ITceRandomSource. The generic package does not own project RNG state.
- Fields:
  - `Chance` (`System.Single`, default `1`): Acceptance probability from 0 to 1.
  - `LookupTarget` (`ZeroEngine.TCE.TceRandomLookupTarget`, default `TriggerSource`): Object used to resolve the random source.

### Cooldown

- Component ID: `zeroengine.tce.condition.cooldown`
- Data type: `ZeroEngine.TCE.CooldownConditionData`
- Runtime type: `ZeroEngine.TCE.CooldownCondition`
- Summary: Prevents repeated accepted executions for a duration.
- Description: Cooldown starts only after every condition has passed, then before effects run, so failed later conditions do not consume cooldown and synchronous reentry is blocked.
- Fields:
  - `Duration` (`System.Single`, default `1`): Cooldown duration in domain time seconds.

### Execution Count

- Component ID: `zeroengine.tce.condition.execution_count`
- Data type: `ZeroEngine.TCE.ExecutionCountConditionData`
- Runtime type: `ZeroEngine.TCE.ExecutionCountCondition`
- Summary: Limits how many accepted executions can pass.
- Description: Use this condition for generic one-shot or limited-use rules. The count increments only after all conditions have passed.
- Fields:
  - `MaxAcceptedExecutions` (`System.Int32`, default `1`): Maximum number of accepted executions allowed.

### Flag

- Component ID: `zeroengine.tce.condition.flag`
- Data type: `ZeroEngine.TCE.FlagConditionData`
- Runtime type: `ZeroEngine.TCE.FlagCondition`
- Summary: Checks a generic flag source.
- Description: Use this condition when a project adapter exposes tags, facts, states, or flags through ITceFlagSource without coupling TCE to a project-specific model.
- Fields:
  - `FlagId` (`System.String`, default `""`): Flag identifier passed to the resolved flag source.
  - `Invert` (`System.Boolean`, default `false`): Invert the flag result before returning the condition result.
  - `LookupTarget` (`ZeroEngine.TCE.TceFlagLookupTarget`, default `Source`): Object used to resolve the flag source.

### Numeric Source

- Component ID: `zeroengine.tce.condition.numeric_source`
- Data type: `ZeroEngine.TCE.NumericSourceConditionData`
- Runtime type: `ZeroEngine.TCE.NumericSourceCondition`
- Summary: Compares a numeric value supplied by the trigger source.
- Description: Use this condition when the trigger source can expose a simple numeric value without depending on a project-specific stat, resource, or damage model.
- Fields:
  - `Comparison` (`ZeroEngine.TCE.TceComparison`, default `GreaterThanOrEqualTo`): Comparison operation applied to the trigger source value.
  - `RequiredValue` (`System.Single`, default `0`): Numeric threshold compared against the trigger source value.

## Effect

### Debug Log

- Component ID: `zeroengine.tce.effect.debug_log`
- Data type: `ZeroEngine.TCE.DebugLogEffectData`
- Runtime type: `ZeroEngine.TCE.DebugLogEffect`
- Summary: Writes a message through the TCE log hook.
- Description: Use this effect in tests, examples, and adapter smoke checks. Production gameplay should prefer project-specific effects.
- Fields:
  - `Message` (`System.String`, default `""`): Log message emitted when the effect runs.
  - `Target` (`ZeroEngine.TCE.TceTargetMode`, default `FromTrigger`): Target actor selection used by the effect.

