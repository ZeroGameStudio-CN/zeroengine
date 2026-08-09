# ZeroEngine TCE

ZeroEngine TCE provides a generic Trigger -> Condition -> Effect runtime for gameplay rules. It is intended for abilities, equipment effects, card effects, passives, relics, and other rules that can be expressed as project-owned triggers, conditions, and effects.

## Boundary

Runtime code in this package must not reference:

- `POB.*`
- Odin / Sirenix
- DOTween
- Unity Input System
- MoreMountains Feedbacks
- PixelCrushers
- weapon, projectile, buff, room, inventory, card, save, session, or player-specific game domains

Game-specific projects should connect to the package through adapter assemblies. Project adapters own domain vocabulary, concrete resource systems, stat systems, event buses, and gameplay effects.

This package is synchronous. Delayed trigger scheduling belongs in a project scheduler adapter after the owning game can route callbacks through its gameplay clock.

## Install

Use the package through Unity Package Manager as a package under:

```text
Packages/com.zerogamestudio.zeroengine.tce
```

Downstream projects should pin a committed ZeroEngine package hash when consuming it through Git.

## Quick Start

```csharp
var graph = new TceGraph();
graph.AddTrigger(new OnInstallTriggerData());
graph.AddCondition(new NumericSourceConditionData
{
    RequiredValue = 1f,
    Comparison = TceComparison.GreaterThanOrEqualTo
});
graph.AddEffect(new DebugLogEffectData { Message = "accepted" });

var runtime = new TceRuntime();
runtime.Install(new NumericValueSource(1f), actor, graph);
```

The project supplies `ITceActor` and, when needed, `ITceClock`. If no clock is provided, `TceRuntime` uses `TceActorClock` and reads `actor.DomainTime`.

## Adapter Contract Smoke Test

Project adapter EditMode tests can reference `ZeroEngine.TCE.EditorTesting`, implement `ITceAdapterContractFixture`, and call:

```csharp
TceAdapterContractAssertions.AssertCoreAdapterContract(fixture);
```

This verifies the adapter's actor liveness, controllable clock behavior, runtime execution, dead-target blocking, and uninstall cleanup.

## Graph Validation

Use `TceGraphValidator.Validate(graph)` before saving or executing authored content. The validator reports structured `TceValidationIssue` values for missing triggers, missing effects, null component data, runtime type mismatches, invalid enum values, and component-specific field errors.

## Generic Components

- `OnInstallTrigger`
- `NumericSourceCondition`
- `CooldownCondition`
- `ExecutionCountCondition`
- `FlagCondition`
- `ChanceCondition`
- `DebugLogEffect`

These components are intentionally small and project-agnostic. Add project-specific triggers, conditions, and effects in adapter packages.

## Component Catalog

The generated catalog lives at:

```text
Documentation~/component-catalog.md
```

Regenerate it from Unity with:

```text
ZGS/ZeroEngine/TCE/Regenerate Component Catalog
```

Every concrete runtime component data type must declare `TceComponentDocAttribute`, and catalog determinism is covered by package tests.

## Samples

Import `Samples~/MinimalGraph` from Package Manager. Add `MinimalTceGraphExample` to an empty GameObject, enter Play Mode, and confirm the configured debug log message is emitted.

Import `Samples~/AuthoringMvp` for the package-manager guide that walks through creating a `TceGraphAsset`, editing it in the graph editor, validating it, and running the generic preview.

## Graph Assets

Create `TceGraphAsset` from `Assets > Create > ZeroEngine > TCE > Graph Asset`.
Graph assets store a runtime `TceGraph` and can be executed by `TceRuntime`.

## Graph Editor

Open `ZGS/ZeroEngine/TCE/Graph Editor` with a `TceGraphAsset` selected. The MVP
editor exposes Trigger, Condition, and Effect lanes, catalog-backed component
creation, validation, saving, and a generic preview route.

## P5 Adapter Boundary

P5 should implement actor, clock, adapter contract tests, and project-specific
bridge components only. It should not duplicate graph assets, catalog,
validation, or editor tooling.

## ZeroEngine Dashboard

The optional Dashboard discovers the graph editor and component-catalog generator through `Editor/ZeroEngineDashboardModule.json`. Existing `ZGS/ZeroEngine/TCE/*` menus remain available and this package does not depend on Dashboard.
