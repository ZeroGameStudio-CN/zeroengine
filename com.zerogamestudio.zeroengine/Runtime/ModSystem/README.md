# ZeroEngine.ModSystem Relocation

ModSystem has graduated into the standalone package:

`Packages/com.zerogamestudio.zeroengine.modsystem`

New projects should depend on `com.zerogamestudio.zeroengine.modsystem` directly.
The broad `com.zerogamestudio.zeroengine` package keeps a package dependency for compatibility, but reusable ModSystem runtime code no longer lives here.

Use:

- `ZeroEngine.ModSystem` for manifest, path safety, source contracts, and safe import orchestration.
- `ZeroEngine.ModSystem.Legacy` only for older `$type` JSON object parsing and singleton loader compatibility.
- `ZeroEngine.ModSystem.Steam` only when Steam Workshop support is installed and enabled.
- `ZeroEngine.ModSystem.Editor` only for legacy editor tools that create/export/validate the older `$type` format.
