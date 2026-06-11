# ZeroEngine.Dlc

ZeroEngine.Dlc is the platform-neutral foundation for DLC and content-pack access checks.

## Runtime Contracts

- `IDlcEntitlementService` answers whether a DLC id is owned and installed.
- `ContentPackCatalog` maps content-pack ids to base-game or DLC requirements.
- `ContentAvailabilityService` checks whether a content pack can be used.
- `LocalDlcEntitlementService` supports Editor, tests, demos, and non-store builds.

## Adapter Boundary

This package does not reference Steamworks, Unity Addressables, platform SDKs, P5, POB, remote catalogs, or CDN APIs.

Storefront adapters should implement `IDlcEntitlementService` in separate packages or project code. Addressables loaders should ask `ContentAvailabilityService` before loading DLC-only keys, but the Addressables handle lifecycle remains owned by the project or a separate Addressables adapter package.

## Minimal Use

```csharp
var entitlements = new LocalDlcEntitlementService();
entitlements.SetEntitlement("dlc.afterfall", DlcEntitlement.OwnedInstalled);

var catalog = ContentPackCatalog.CreateInMemory(new[]
{
    new ContentPackDefinition("chapter.afterfall", false, "dlc.afterfall", "Afterfall")
});

var availability = new ContentAvailabilityService(catalog, entitlements);
var result = availability.CanUseContent("chapter.afterfall");
```
