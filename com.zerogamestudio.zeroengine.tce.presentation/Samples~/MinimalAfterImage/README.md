# Minimal AfterImage

This sample shows the reusable ZeroEngine side of a visual-only TCE effect.

Use `SpawnSnapshotEffectData` in a graph with an install or project trigger. The effect resolves the target actor, captures its Unity renderer state, and plays a short visual snapshot. It does not apply damage, spawn projectiles, change stats, or dispatch gameplay events.
