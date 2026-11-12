# Portrait Speech

Reusable, text-driven pixel portrait speech for Unity UI. The package has no POB,
dialogue-plugin, audio, gameplay clock or gameplay RNG dependency. Five speaking
silhouettes blend back into the original closed mouth. This is stylized visible
character synchronization, not phoneme or recorded-audio lip sync.

Version 0.1.2 preserves the consumer's committed expression transitions, authored
bridges and protected blink rendering, together with its public types, serialized
data and asset GUIDs. Install from the canonical ZE Git repository at the same
tested commit as the other selected ZE packages. POB integration remains in POB.

## Configure

Create **ZeroEngine / UI / Portrait Speech Library**, then select it and open
**ZGS / 工作台 → 对话头像口型**（也可从 Library Inspector 打开）. Select Sprite assets in Project and add
them to the current actor. Generate drafts, inspect each mouth rectangle and skin
sample, then save. Missing-only batch saves preserve existing calibration. Source
PNGs and texture Read/Write settings are never modified.

Coordinates use the full source sprite canvas, bottom-left origin, normalized
0–1. Runtime compensates Unity's transparent-border trimming and atlas UVs.
Actor ID plus exact sprite name identifies a profile; an unknown portrait stays
static. A subclass may supply face bounds through `TryGetFaceBounds`.

For tiny mouths, enable **按原图像素配置口型** in the authoring window. Each of the
five poses has source-pixel width/height and center offsets. This keeps small
open/round/wide/tight silhouettes distinct after rasterization. The mouth rectangle
is the safe edit region; idle always restores the original pixels. The five vectors
are stored in the same profile and shared by runtime and preview.

Open **批量管理** to pause or resume mouth and/or blink playback for the current
actor or the whole library. This keeps each portrait's calibration and respects
expressions with no active channel. The same tab exposes shared speech and blink
timing. Editing and GIF preview remain available for paused portraits; pausing
does not change the surrounding application's preview gate.

For expressive portraits, assign an optional **手绘口型贴片**. It is a
point-filtered, uncompressed RGBA texture at the native mouth rectangle width and
twice its height: closed at the top, half open at the bottom. Transparent pixels
leave the original artwork visible. Idle and maximum opening use the portrait;
the typewriter switches complete drawings without deforming or blending lips.
An unassigned sheet keeps the standard generated shapes.

Open **复杂嘴形 · 候选与像素修正** in the mouth tab. Frame the native mouth and
sample intact cheek skin, then **生成两档候选** reconstructs two small drafts from
the dominant connected lip and its palette. These are candidates: inspect the
expression, then paint the half-open/closed pixels in the same window. The left
grid samples source colors; the right grid paints, samples, or restores original
pixels. Brush strokes have local undo/redo. **编辑当前贴片** copies an assigned
sheet without changing its PNG. Drafts survive portrait selection within the open
window; closing the window discards them.

The full portrait and GIF use the same unsaved draft. **保存新贴片并应用** creates
a new point-filtered, uncompressed PNG and links only the selected profile. It
refuses an existing path, changed source, or stale profile; the earlier sheet
remains intact. Unity Undo restores the library reference, while the new PNG
remains available. Unsaved textures cannot be written by ordinary batch saves.

**寻找可复用的同角色差分** compares actual source mouth pixels plus a two-pixel
border, rather than canvas size. Only matching same-actor variants become reuse
candidates. Preview each destination and confirm before applying. Reimported
source art, changed library data, or an altered sheet requires review again.
No runtime, save-game, or project-specific dependency is added by this editor flow.

## Whole-library maintenance

Use **全库更新 → 扫描全库变化 → 一键生成待更新候选**. The source GUID/hash,
canvas size, calibration, pose sheet and generator recipe are recorded inside the
same library's Editor-only state after confirmation. Runtime playback does not
load or poll this record. A shared timing change already affects every portrait;
it does not rebuild art.

Missing complex-mouth sheets and stale generated sheets can produce candidates
in one batch. Preview each destination, correct it if needed, and confirm before
**保存并应用已确认项**. Application rechecks source and calibration fingerprints
before and after PNG imports. Editing a confirmed candidate requires confirmation
again. Unconfirmed entries and paused channels stay untouched.

Existing or externally edited sheets are treated as manual work and protected.
Changed canvas size needs recalibration. A changed portrait can invalidate eye
occlusion as well as lip pixels: review both channels before confirming its new
baseline. The tool creates new PNGs and preserves earlier files; an import error
keeps unlinked outputs listed for recovery. Source-art matching and explicit
review are still required; a batch button does not establish visual acceptance.

## Expression sprites

An optional **Portrait Expression Set** maps explicit content keys to existing
Sprite variants and one shared portrait library/actor. Assign it to
PortraitSpeechImage and call SetExpression("smile"). A successful switch selects
that sprite's own speech/blink profile and resets both presentation clocks.
Unknown keys, duplicate keys or missing sprites return false and leave the
current expression untouched. An uncalibrated sprite displays statically rather
than inheriting a previous face's mouth. ExpressionChanged lets a project add
content-specific screen/character feedback; it does not infer emotion from text.
Existing users of the inline profile need no expression set.

Use **TransitionToExpression("smile")** to opt in to a soft expression hand-off.
Assign a shared non-empty **Transition Group** only to the same actor, outfit and
framing. Mark each pose's eyes as **Open** or **Closed**. Open poses need a saved
blink calibration; a paused calibration is used only during the transition.
Unspecified, mismatched or incompatible poses use the ordinary immediate switch.

The outgoing mouth settles and the eyes close. A short local face hand-off
blends skin and brows before switching the sprite. A single closed eyelid moves
toward the target calibration; protected foreground pixels stay in place. An
authored closed pose can optionally provide an eye box, skin sample and closed
contour while its blink playback stays off. When aligned, its painted line joins
the moving eyelid before the sprite switch. Open target eyes only reopen after
the swap, using their own calibration. The mouth blends separately in its small
calibrated region. Subsequent glyphs still drive speech. **SetExpression** remains the
immediate path, and **Complete(true)** finishes a pending hand-off immediately.
**ExpressionChanged** fires at the visible sprite change. The shared timings live
on the expression set. No portrait textures are generated or edited.

For an expression pair that needs a hand-drawn in-between pose, add a **Bridge**
on the same expression set. Select the exact **From** and **To** keys, then
assign a transparent **Patch** texture and its **Bottom Left** origin in the
untrimmed portrait's source pixels. A tightly cropped patch avoids storing the
whole portrait; a full-size transparent patch can use origin (0, 0). Import at
native size (NPOT scaling off), with alpha, point filtering and no mipmaps.
Paint only the pixels that should change. The portrait's silhouette and
calibrated mouth stay intact.

The authored pose plays automatically just before the swap (about 20 ms, or
one rendered update when a slow frame would skip it). A missing, duplicate or
out-of-bounds bridge keeps the ordinary transition. Reversing a pair requires
its own entry. Adding a bridge does not enable transitions on **SetExpression**
or change either source portrait.

A host can call **TryResolveExpressionPair** on its portrait speech library to
automatically select compatible changes. Set one **Expression Group** per actor
and outfit, with a case-sensitive sprite prefix for registered artwork. Both
sprites need library profiles and the same canvas, pivot and pixels per unit.
Mark existing static squints in **Closed Poses** and place composition changes
in **Excluded Poses**. Unknown poses, overlapping groups and pairs with two
static endpoints stay on the immediate path. An explicitly requested
**TransitionToExpression** still supports its own authored group and bridge.

## Connect

For an ordinary UI Image, add `PortraitSpeechImage`, assign its profile, call
`RevealVisibleCharacter(character, index)` from the typewriter's visible-character
event, and call `Complete()` at natural completion or `Complete(true)` on skip.
Disabling restores the original Image material; sprite changes clear old speech.

Existing presentation controllers may instead own `PortraitSpeechMotion` and
apply it through `PortraitSpeechMaterial.Apply`. Pass an unscaled presentation
clock, call `Reset` when actor/line/owner changes, and restore owned materials on
disable. Do not feed markup tags or invisible characters to the driver.

Runtime assembly: `ZeroEngine.PortraitSpeech`. Authoring assembly:
`ZeroEngine.PortraitSpeech.Editor`. The latter is Editor-only. The Resources shader
is included for Player builds without project-specific shader registration.

POB's **ZGS / 工作台**, search **对话头像口型**, opens its existing unified style
and actor database through a typed adapter. The unified style Inspector also has
a **头像口型配置与预览** button. POB owns glass, framing, dialogue events and the
preview switch; package code owns speech and calibration.

`PortraitBlinkMotion.Evaluate` provides an independent presentation-clock eye
envelope: fast closure, a brief fully closed hold, slower reopening and bounded
deterministic interval variation. It has no transform animation, queued catch-up
or gameplay RNG usage.

Open the **眨眼** tab in the same authoring window to frame either eye, sample the
eyelid skin, protect foreground pixels, and scrub closure or loop it alongside
speech. Save affects only the blink part of the selected portrait profile. The
copy action stages same-actor, same-size variants for review, preserving existing
mouth and eye calibrations. Preview each destination, check **确认**, then use
**只写入已确认**. Editing the marked part, changing the sprite or reimporting
its source requires review again. Other drafts remain pending. Generating a
mouth draft for a different canvas samples that canvas's own pixels.

**导出 GIF** renders the current draft directly with the same preview shader.
The mouth tab exports the speech demo. The blink tab exports a blink and includes
speech when **播放口型** is selected. The loop uses a dark backdrop and omits
authoring guides. It needs no Recorder, PlayMode, or external encoder and does
not save the draft to the library.
An invalid region or an export with no visible change reports what to adjust.
An existing GIF is preserved when export fails.

The editor and runtime share the same shader and blink profile. A consumer using
`PortraitSpeechImage` gets blink from the enabled profile; a custom presentation
can own one `PortraitBlinkMaterial`, apply it to the portrait and its rendered
stencil material, then dispose it when the portrait closes or is disabled.

For a same-speaker expression with compatible shared mouth/eye calibration, call
SetExpression(key, true) to keep the current speech pose and blink clock. This
opt-in overload does not start a new dialogue line. The existing one-argument
method retains its reset behavior for actor/line changes.


## Consolidation dependency correction

The package explicitly declares Unity's built-in
JSON Serialize module (`com.unity.modules.jsonserialize`) for this package's
direct `JsonUtility` calls. Consumers no longer need an unrelated installed
package to supply that module.
