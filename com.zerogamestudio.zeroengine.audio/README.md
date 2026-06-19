# ZeroEngine.Audio

Audio management system.

## Features

- Audio pooling and management
- Volume control with persistence
- Music/SFX separation

## Designer Config Validation

`ZeroEngine.Audio.Editor` provides `AudioConfigValidator` for audio cues and
music tracks. It reports missing clips, invalid volume/pitch ranges, invalid
spatial blend/cooldown values, and music tracks without playable clips.

## Dependencies

- `com.zerogamestudio.zeroengine.core` - Core utilities
- `com.zerogamestudio.zeroengine.persistence` - Settings persistence

## Version

2.0.0 - Initial modular release (split from ZeroEngine v1.17.0)

## Dependency Pinning

When this package is consumed through Git UPM, add every
`com.zerogamestudio.*` dependency from `package.json` to the consumer project's
`Packages/manifest.json` at the same tested commit. See
[Consumer Project Setup](../docs/consumer-project-setup.md).
