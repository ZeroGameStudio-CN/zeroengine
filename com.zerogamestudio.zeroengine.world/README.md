# ZeroEngine.World

World and environment systems.

## Modules

- **Environment** - Weather and day/night cycle system
- **Calendar** - In-game calendar and time system
- **Minimap** - Minimap rendering and markers

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Save/Load support

## Config Validation

`ZeroEngine.World.Editor` provides `WorldConfigValidator` for Editor tests and release checks:

- `CalendarEventSO` event IDs, duplicate IDs, display names, dates, times, recurrence, level gates, and reminders.
- `WeatherPresetSO` weather type duplication, descriptions, fog density, lighting multiplier, transition duration, and audio volume.
- `DayNightPresetSO` curves, gradients, sun intensity, sunrise/sunset angles, and skybox assignment.

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
