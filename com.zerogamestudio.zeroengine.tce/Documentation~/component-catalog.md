# ZeroEngine TCE Component Catalog

## Trigger

### On Install

- Data type: `ZeroEngine.TCE.OnInstallTriggerData`
- Runtime type: `ZeroEngine.TCE.OnInstallTrigger`
- Summary: Fires once when the graph is installed.
- Description: Use this trigger for immediate setup rules that should run after all conditions and effects are initialized.
- Fields:
  - `Order` (`System.Int32`, default `0`)

## Condition

### Chance

- Data type: `ZeroEngine.TCE.ChanceConditionData`
- Runtime type: `ZeroEngine.TCE.ChanceCondition`
- Summary: Passes based on a deterministic random source.
- Description: Use this condition when a trigger, owner, or install source exposes ITceRandomSource. The generic package does not own project RNG state.
- Fields:
  - `Chance` (`System.Single`, default `1`)
  - `LookupTarget` (`ZeroEngine.TCE.TceRandomLookupTarget`, default `TriggerSource`)

### Cooldown

- Data type: `ZeroEngine.TCE.CooldownConditionData`
- Runtime type: `ZeroEngine.TCE.CooldownCondition`
- Summary: Prevents repeated accepted executions for a duration.
- Description: Cooldown starts only after every condition has passed, then before effects run, so failed later conditions do not consume cooldown and synchronous reentry is blocked.
- Fields:
  - `Duration` (`System.Single`, default `1`)

### Execution Count

- Data type: `ZeroEngine.TCE.ExecutionCountConditionData`
- Runtime type: `ZeroEngine.TCE.ExecutionCountCondition`
- Summary: Limits how many accepted executions can pass.
- Description: Use this condition for generic one-shot or limited-use rules. The count increments only after all conditions have passed.
- Fields:
  - `MaxAcceptedExecutions` (`System.Int32`, default `1`)

### Flag

- Data type: `ZeroEngine.TCE.FlagConditionData`
- Runtime type: `ZeroEngine.TCE.FlagCondition`
- Summary: Checks a generic flag source.
- Description: Use this condition when a project adapter exposes tags, facts, states, or flags through ITceFlagSource without coupling TCE to a project-specific model.
- Fields:
  - `FlagId` (`System.String`, default `""`)
  - `Invert` (`System.Boolean`, default `false`)
  - `LookupTarget` (`ZeroEngine.TCE.TceFlagLookupTarget`, default `Source`)

### Numeric Source

- Data type: `ZeroEngine.TCE.NumericSourceConditionData`
- Runtime type: `ZeroEngine.TCE.NumericSourceCondition`
- Summary: Compares a numeric value supplied by the trigger source.
- Description: Use this condition when the trigger source can expose a simple numeric value without depending on a project-specific stat, resource, or damage model.
- Fields:
  - `Comparison` (`ZeroEngine.TCE.TceComparison`, default `GreaterThanOrEqualTo`)
  - `RequiredValue` (`System.Single`, default `0`)

## Effect

### Debug Log

- Data type: `ZeroEngine.TCE.DebugLogEffectData`
- Runtime type: `ZeroEngine.TCE.DebugLogEffect`
- Summary: Writes a message through the TCE log hook.
- Description: Use this effect in tests, examples, and adapter smoke checks. Production gameplay should prefer project-specific effects.
- Fields:
  - `Message` (`System.String`, default `""`)
  - `Target` (`ZeroEngine.TCE.TceTargetMode`, default `FromTrigger`)

