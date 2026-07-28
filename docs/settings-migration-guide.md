# Settings migration guide

Migrate only when `ZeroEngine.Settings.Primary` is absent. Validate through the
new catalog, save the new document, then set an import marker. Keep all legacy
keys/files unchanged so an older build can still start.

## Standard IDs

| Legacy meaning | New ID |
| --- | --- |
| Language | `localization.locale` (locale code, never an index) |
| Window mode | `display.windowMode` |
| Resolution width/height | `display.width`, `display.height` |
| Refresh rate | `display.refreshRate` |
| VSync | `display.vSyncCount` |
| Frame cap | `display.frameRateLimit` |
| Quality | `display.quality` (quality name, never an index) |
| Master/music/SFX | `audio.master`, `audio.music`, `audio.sfx` |
| Mouse/stick sensitivity | `input.pointerSensitivity`, `input.gamepadSensitivity` |
| Stick deadzone/invert Y | `input.gamepadDeadzone`, `input.invertY` |
| Rebind data | `input.bindingOverrides` |

## POB

- Load every legacy per-action override into the same `InputActionAsset`, then
  export one `SaveBindingOverridesAsJson()` value.
- Keep keyboard/mouse, generic gamepad and optional touch as binding groups.
  Xbox, PlayStation, Nintendo and Steam Deck remain glyph presentation only.
- Map project-only FOV, reticle, damage-number and effect settings to
  `pob.*` IDs rather than adding them to the standard catalog.

## LLS

- Resolve legacy `LanguageIndex` against the exact old locale ordering and save
  the resulting locale code.
- Map its global audio/display fields to the standard IDs above.
- Keep project-only gameplay and presentation preferences under `lls.*`.

## GalleryKeeper

- `languageCode` maps to `localization.locale`.
- Existing volume, display, look sensitivity, deadzone, invert-Y and binding
  JSON map to the standard IDs.
- FOV, precision-aim and other Gallery-only preferences use `gallery.*`.
