# ZeroEngine.Audio

Unity-native audio management with stable event ids.

## Features

- `AudioCueSO` random clip, volume and pitch variants
- pooled 2D/3D SFX with configurable rolloff, distances, pan, Doppler, spread, reverb, cooldown, and concurrency limits
- intro/loop music with per-track or manager-default unscaled-time crossfades and fade-outs
- `AudioBankSO` registration for Addressables-style content scopes
- `UnityAudioEventService` facade for project-owned semantic event ids, registered as the unique enabled `IAudioEventService` owner
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

Consumers resolve the active owner through `ServiceRegistry`. Enabling a second
`UnityAudioEventService` fails closed and disabling the owner unregisters it, so
scene-scoped consumers never retain a stale service.

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

2.2.1 - Unique runtime `IAudioEventService` ownership with deterministic
enable/disable registration.


## Owned playback (2.3)

`IAudioEventService` remains unchanged. The default Unity facade additionally implements
`IAudioPlaybackService`. `PlayScoped(id, position)` returns a backend-neutral `IAudioPlayback`,
or null for an unknown/invalid/rejected request. `IsCurrent` describes resource ownership,
not audible output; `IsLooping` records the authored loop policy. `TryStop` and `Dispose`
stop only the returned playback and are safe after completion, reuse or manager destruction.

Direct backends can use `AudioManager.TryPlaySFX(cue, out playback, position)` and
`PlayMusicScoped`. Ordinary legacy SFX calls allocate no playback handles. All existing
start paths advance the same emitter/music generations, including raw clips and repeated
requests for the same music. Old raw-clip timers cannot return a reused emitter.

Music handles own one intro/loop/crossfade transition. A later music request transfers
ownership even when it reuses the same track; disposal of the former handle cannot stop it.
Music disposal is immediate. Legacy `Stop(eventId)` remains a cue-level operation; it only
stops music if that event resolves to the current music definition. Use scoped playback
for local cleanup instead of stopping every voice mapped to an event.

One-shots may be left with the manager to finish naturally. Retain looping playback in the
calling lifetime and dispose it on cancellation/disable/exit. Use backend start methods,
rather than changing a borrowed emitter's AudioSource directly, to transfer ownership.
No project, TCE/Buff, middleware SDK or new asset schema dependency is introduced.
