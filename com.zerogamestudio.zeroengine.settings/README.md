# ZeroEngine.Settings

Versioned global player settings with one transactional service for language,
display, audio, controls, binding overrides and accessibility signals.

Runtime APIs use the `ZeroEngine.PlayerSettings` namespace so they can coexist
with the deprecated slot-oriented types historically shipped by Persistence.

## Ownership

The package owns validation, preview/commit/cancel/reset, migration, backup
recovery, the complete `StandardSettingsCatalog`, standard adapters and a
complete UGUI fallback menu. A standard consumer enables the whole catalog.
The game owns defaults, project-specific additive definitions, InputActionAsset,
AudioMixer and optional localization/theme overrides.

Create a project `SettingsBootstrap` subclass, build a `SettingsCatalog`, choose
one `ISettingsStore`, register appliers, then wait for `Ready` before showing the
main UI. Start and pause settings screens must open sessions from the registered
`ISettingsService`; they must not keep separate copies.

`PlayerPrefsSettingsStore` defaults to:

- `ZeroEngine.Settings.Primary`
- `ZeroEngine.Settings.Backup`

Use `InputBindingService` from ZeroEngine.Input 2.1 for GUID-based keyboard,
mouse and generic gamepad rebinding. Save its whole-asset override JSON in
`input.bindingOverrides`.

## Fallback settings UI

`ZeroEngine.PlayerSettings.UI.StandardSettingsUiBuilder` is the default entry
point. It always creates the same controls, display, audio and accessibility
baseline, including language, display mode/resolution/refresh rate, separate
audio buses and a rebinding entry. It returns controls keyed by
`StandardSettingIds`; games bind values and append project-specific rows to the
returned categories.

```csharp
StandardSettingsUiView view =
    new StandardSettingsUiBuilder(host, font, theme)
        .Build(StandardSettingsUiText.SimplifiedChinese);

view.Slider(StandardSettingIds.PointerSensitivity)
    .onValueChanged.AddListener(SetPointerSensitivity);
view.Layout.CreateSliderRow(
    view.Category(StandardSettingsUiCategory.Display),
    "Project Field Of View",
    "Field Of View",
    out _);
view.Rebuild();
```

`SettingsUiLayoutBuilder` remains available as the lower-level layout API for a
fully custom menu. It creates a responsive title, equal-width tabs, scrollable
categories, aligned rows and a fixed footer using anchors and layout groups.

Pass a localized `Font` and an optional `SettingsUiTheme`. With no theme, the
builder uses a code-only palette, built-in font and plain UGUI graphics, so a
new project always has a usable fallback. Games keep their existing settings
listeners and can replace only theme assets and copy.

`SettingsRebindUiLayoutBuilder` provides the matching fallback remapping
surface. It uses device-family tabs, a scrollable action list, fixed footer and
selection auto-scroll, so action count does not change the page geometry.
Projects provide device tabs and binding callbacks; desktop games can expose
keyboard/mouse and gamepad, while platform-specific games can expose only the
families they support. Composite binding parts can be formatted with
`InputBindingService.GetBindingDisplayString(actionId, bindingIds)` as labels
such as `LB + X`.

See `docs/specs/2026-07-27-zeroengine-settings-platform.md` in the ZeroEngine
repository for the complete contract and migration rules.
