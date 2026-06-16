# Toast System

ZeroEngine.UI.Toast is a reusable in-game notification system for gameplay alerts, system messages, warnings, and lightweight confirmations.

## Responsibilities

- Stack multiple simultaneous notifications.
- Apply overflow, duplicate, priority, and group clear policies.
- Route toasts to multiple visual anchors.
- Render through a default UGUI/TextMeshPro presenter.
- Auto-create a default runtime overlay presenter if no presenter has been configured.
- Expose a project-independent static facade.
- Let each game own localization and project-specific wording.

## Basic API

```csharp
using ZeroEngine.UI.Toast;

Toast.Show("Copied");
Toast.Success("Quest complete");
Toast.Warning("Not enough gold");
Toast.Error("Upload failed");
Toast.ClearAll();
```

No scene setup is required for the default runtime path. Projects can still install or author a `ToastRootPresenter` prefab when they need a custom canvas, sorting order, or art treatment.

## Rich API

```csharp
Toast.Show(new ToastRequest
{
    Message = "Inventory full",
    Severity = ToastSeverity.Warning,
    Priority = ToastPriority.High,
    Anchor = ToastAnchor.TopRight,
    DedupeKey = "inventory_full",
    GroupKey = "inventory",
    Duration = 2.5f
});
```

## Project Localization

Resolve localized text in the project adapter before calling `Toast.Show`, or implement `IToastTextResolver` and pass it to `Toast.Configure`.
