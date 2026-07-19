# ZeroEngine.Audio

Unity-native audio management with stable event ids.

## Features

- `AudioCueSO` random clip, volume and pitch variants
- pooled 2D/3D SFX with configurable rolloff, distances, pan, Doppler, spread, reverb, cooldown, and concurrency limits
- intro/loop music with per-track or manager-default unscaled-time crossfades and fade-outs
- `AudioBankSO` registration for Addressables-style content scopes
- `UnityAudioEventService` facade for project-owned semantic event ids
- AudioMixer Master/BGM/SFX volume control
- optional SaveManager persistence for projects without their own settings model

## Dependencies

- `com.zerogamestudio.zeroengine.core`
- `com.zerogamestudio.zeroengine.persistence`

## Setup

1. Add `AudioManager` and `UnityAudioEventService` to an application-owned GameObject.
2. Configure the AudioMixer and default music/SFX groups on `AudioManager`.
3. Create `AudioCueSO`, `AudioMusicSO`, and `AudioBankSO` assets.
4. Register a loaded bank with `UnityAudioEventService.RegisterBank`.
5. Play semantic ids through `IAudioEventService.Play`.
6. Stop all loop instances mapped to an id with `IAudioEventService.Stop`.

Addressables is intentionally not a package dependency. A consumer loads an
`AudioBankSO`, registers it for the relevant content scope, stops its looped
events, unregisters it, and only then releases the Addressables handle.

Projects with their own settings persistence should call
`AudioManager.Configure(..., persistVolumeWithSaveManager: false)` and apply
their saved Master/BGM/SFX values after configuration.

Project-level playback defaults can be supplied with
`AudioManager.ConfigurePlayback(crossFadeTime, musicFadeOutTime, initialPoolSize,
maximumPoolSize)`. Set an `AudioMusicSO` transition value to `-1` to inherit
those defaults.

## Version

2.2.0 - Configurable spatial SFX, per-track music transitions, and project-level
music/pool playback defaults.
