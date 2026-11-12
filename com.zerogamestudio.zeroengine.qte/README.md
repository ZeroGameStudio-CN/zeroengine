# ZeroEngine.Qte

Timing-window and hold/release challenges with no Unity engine, UI, input-system,
persistence or game dependency. The runtime assembly is `ZeroEngine.Qte`.

The caller owns input, gameplay time, random sampling, difficulty resolution and
lifetime. Supply resolved `QteSettings` to `QteSession.Begin`, call `Press` for a
fresh action, and advance with the caller's delta and held state. A session freezes
its settings at Begin. Invalid/non-positive deltas do not advance it.

`Snapshot` is an immutable view value; a HUD never needs the session, scene or
game controller. Success/failure/cancellation is terminal until Reset/Begin.
Cancel preserves the cancelled outcome; Reset clears the session for reuse.
No global manager, event bus, automatic input binding or renderer is installed.

Use a game adapter for rewards, localized copy, difficulty profiles and a local UI
skin. An absent HUD must not silently change the chosen challenge mode.

Install through UPM from the official repository with a tested full commit:
`https://github.com/ZeroGameStudio-CN/zeroengine.git?path=com.zerogamestudio.zeroengine.qte#<tested-commit>`.
Run `ZeroEngine.Qte.Tests.QteSessionTests` before updating a consumer pin.
