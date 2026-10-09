# ZeroEngine Render

Small UnityEngine-only rendering primitives. No TCE, gameplay, pooling service,
singleton, project asset or third-party plugin dependency.

## SpritePoseHistory

Construct with distinct, caller-owned `SpriteRenderer` targets. `TryCapture`
freezes a non-null sprite's frame, world TRS, flips, color and sorting into one
fixed-capacity record. Only Simple draw-mode sprites are supported; sliced/tiled
renderers are rejected. Duration and tint are per-capture inputs. A full buffer
returns false unless `replaceOldest` is explicitly true. Invalid capture leaves
the old playback unchanged; passing a target as source is rejected.

Call `Advance(delta, intensity)` with the owner's clock. Zero, negative or nonfinite
delta does not advance age. Intensity is clamped and changes visibility only.
Expiry and `Clear` disable targets and release borrowed sprite references, without
destroying renderers, textures or materials. Source reuse/destruction cannot alter
an existing record. Capture/playback after construction has no managed allocation.
TRS does not promise exact shear reconstruction. Material choice stays with targets.

POB keeps its serialized sampling component and delegates just snapshot playback.
Existing TCE Presentation owns one-shot mesh/layered/ghost graph effects; this
high-frequency fixed-capacity path does not replace that runner or duplicate a TCE
effect system.

## FlowPattern.hlsl

Include `Runtime/Shaders/FlowPattern.hlsl`. Scalar functions use the same source and
GPU vectors as Godot `zero_render/flow_pattern.gdshaderinc`. Distances/periods share
the caller's spatial unit and phase is in cycles. Closed periods fit an integer
number of repetitions; wrapped dash distance treats both ends and the loop seam
equally. Tracer, open-line dash and chevron functions preserve POB's current math.
Inputs must be finite. Geometry, AA footprint, style, material and time are supplied
by the caller; the include never reads engine time or scene resources.

## Verification

Run the `ZeroEngine.Render.Tests.Editor` assembly, plus affected consumer tests.
It checks bounded lifetime/overflow, snapshot independence, cleanup, invalid input,
managed allocations and actual GPU results against `render_flow_vectors.json`.
Those vectors must match the Godot fixture byte-for-byte. Actual gameplay camera
checks are additionally required for visible consumer changes. Package tests do
not prove platform/export or human visual acceptance.

Consume through the official ZeroEngine Git UPM URL pinned to a tested commit.
Local file references are temporary verification only, never shared-project pins.

2026-09-12 Windows verification (Unity 2022.3.62f3, URP 14.0.12): package EditMode
8/8 including 20 GPU vectors and allocation checks; POB blade/telegraph regression
85/85; isolated real-entry PlayMode 1/1 (Intro, base, three warning shapes, stop and
cleanup). Actual game-camera captures were inspected. The test kept 94 monitored
configuration/material/settings files unchanged; its temporary fixture was removed.
Godot counterparts passed their own 468 tests and 20 GPU vectors, and the independent
game's pinned adoption passed 292 tests after its separate naming/asset cleanup.
These results do not qualify other render pipelines, platforms or exported Players.
