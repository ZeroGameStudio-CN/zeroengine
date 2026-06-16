# Adapter Template Sample

Use this template when a project wants to consume TCE without copying package internals.

## Runtime Adapter Shape

Project adapters should provide:

- an `ITceActor` implementation that reports liveness, domain time, and the native project object;
- an `ITceClock` implementation when gameplay time is not Unity `Time.time`;
- a small event bridge that turns project events into TCE triggers;
- project-specific component data only in the project adapter package.

The adapter depends inward on `ZeroEngine.TCE`. The TCE package does not depend on the adapter.

## Contract Test Shape

Editor tests can use the package contract helper:

```csharp
using NUnit.Framework;
using ZeroEngine.TCE.EditorTesting;

public sealed class ProjectTceAdapterContractTests
{
    [Test]
    public void ProjectAdapter_SatisfiesCoreTceContract()
    {
        TceAdapterContractAssertions.AssertCoreAdapterContract(new ProjectTceAdapterFixture());
    }
}
```

The fixture implements `ITceAdapterContractFixture` by creating an alive actor, a dead actor, and a mutable clock. Project content such as cards, weapons, rooms, buffs, inventory, save state, and localization stays outside the reusable TCE package.
