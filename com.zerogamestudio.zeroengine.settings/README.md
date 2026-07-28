# ZeroEngine.Settings

Versioned global player settings with one transactional service for language,
display, audio, controls, binding overrides and accessibility signals.

Runtime APIs use the `ZeroEngine.PlayerSettings` namespace so they can coexist
with the deprecated slot-oriented types historically shipped by Persistence.

## Ownership

The package owns validation, preview/commit/cancel/reset, migration, backup
recovery and standard adapters. The game owns defaults, enabled definitions,
InputActionAsset, AudioMixer, localization content, UI layout and text.

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

See `docs/specs/2026-07-27-zeroengine-settings-platform.md` in the ZeroEngine
repository for the complete contract and migration rules.
