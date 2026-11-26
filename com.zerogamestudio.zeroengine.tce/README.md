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

Version 0.2.1 also requires a direct Git pin for `com.zerogamestudio.zeroengine.editor-ui@1.3.0` at the same ZeroEngine commit. It localizes Dashboard labels, tooltips, and the write confirmation to Simplified Chinese without changing menu paths. Git UPM dependencies do not resolve same-repository sibling packages transitively.

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

### Host-owned execution state (0.3.0)

`ExecutionCountConditionData.StateKey` optionally binds the accepted count to an
`ITceStateSource` on the install source or actor. Its `ITceStateStore` exposes
runtime numbers/text only. The host owns stable identities, reset scope, capture,
restore and storage; TCE never calls a save API or depends on a Buff implementation.
An empty key keeps the existing installation-local counter. A nonempty key without
a state source fails explicitly. Accepted executions consume after all conditions
pass and before effects, including synchronous reentry; reinstalling does not reset
host-owned state. Future host adapters may back that contract with Buff state.

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

Regenerate it from Unity by opening `ZGS > 工作台 > 数据与本地化`, enabling `高级工具`, and running:

```text
重新生成组件目录
```

Every concrete runtime component data type must declare `TceComponentDocAttribute`, and catalog determinism is covered by package tests.

## Samples

Import `Samples~/MinimalGraph` from Package Manager. Add `MinimalTceGraphExample` to an empty GameObject, enter Play Mode, and confirm the configured debug log message is emitted.

Import `Samples~/AuthoringMvp` for the package-manager guide that walks through creating a `TceGraphAsset`, editing it in the graph editor, validating it, and running the generic preview.

## Graph Assets

Create `TceGraphAsset` from `Assets > Create > ZeroEngine > Data and Graphs > TCE > Graph Asset`.
Graph assets store a runtime `TceGraph` and can be executed by `TceRuntime`.

## Graph Editor

Open `ZGS > 工作台 > 内容创作 > TCE 图编辑器` with a `TceGraphAsset` selected. The MVP
editor exposes Trigger, Condition, and Effect lanes, catalog-backed component
creation, validation, saving, and a generic preview route.

## P5 Adapter Boundary

P5 should implement actor, clock, adapter contract tests, and project-specific
bridge components only. It should not duplicate graph assets, catalog,
validation, or editor tooling.

## ZeroEngine Dashboard

The optional Dashboard discovers the graph editor and component-catalog generator through the schema v2 descriptor and invokes this package's typed provider. This package does not depend on Dashboard.

## Explicit execution contexts

`TceInstallationContext`, `TceEventContext` and `TceExecutionContext` separate an installed binding, captured event facts and one execution. Pass execution contexts explicitly across conditions, effects, async continuations and child rules. Each execution owns its transient values and commit notifications; completing or canceling a frame prevents new contributions. Events retain event/root/cause IDs, separate subject/target/instigator/emitter references and captured position/direction.

Admission costs use `TryReserveResource(owner, key, amount, available, consume)`: reservation never spends the resource, and requests for the same captured owner/key share a total. After every condition accepts, call `TryCommit(commitState)`; it rechecks all totals before any payment, applies the adapter's non-rejecting synchronous payments and ordinary state commits, then publishes deferred callbacks. A refused admission completes its context without committing. Availability callbacks must be pure, and payment adapters must defer lifecycle/event callbacks through the supplied sink so one payment cannot invalidate a later payment halfway through the batch. `Commit()` remains the throwing convenience for callers that do not expect resource refusal. Buff definitions, actual layer mutations and save policy remain host responsibilities.

Adapters implement `ITceObjectLifetime`. `TceObjectRef` compares identity, activation and world before resolving a native object. Identity validity and active/living policy are separate: the effect decides whether an inactive or dying object supports its operation. Never reconstruct a retained reference from a pooled object's new activation.

`TceAbilityGrantRegistry` shares an installed child among source owners and releases only the retiring source. It has no duration or charge counter; the host Buff/state system owns those. `TceEffectScope` owns replaceable per-recipient contributions. `TceContributionBatch` selects the strongest contribution in each group and closes before dispatch. `TceAreaGeometry` supplies circle/box segment clipping without a project physics dependency. `ITceVisualTargetReceiver` is an optional explicit target injection point for feedback adapters; it does not spawn or own visuals.

POB consumes these contracts from its existing AbilityHandler and supplies Core/projectile identity, Buff, motion, damage and resource adapters. No POB types or save backend are introduced into this package. The generic graph runtime and its host-owned `ITceStateStore` remain independently usable.

Grant installation reserves its receiver/definition/slot before invoking host code. Reentrant installation of that same key is refused; revocation, receiver recycling or clearing during installation releases the returned child instead of retaining an orphan. Clearing rejects grants from retiring children and attempts every disposal before reporting aggregated errors. Host adapters bind receiver-end events (including object-pool return) and keep the existing ability installer/uninstaller; the registry adds no projectile implementation, MonoBehaviour or duration timer.
