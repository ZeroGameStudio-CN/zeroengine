using System;
using UnityEngine;

namespace ZeroEngine.Render
{
    /// <summary>Fixed-capacity sprite snapshots. The caller owns renderers and the clock.</summary>
    public sealed class SpritePoseHistory
    {
        private readonly SpriteRenderer[] targets;
        private readonly Entry[] entries;
        private int next;

        private struct Entry
        {
            public bool Active;
            public float Age, Duration;
            public Sprite Sprite;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool FlipX, FlipY;
            public int SortingLayer, SortingOrder;
            public Color Color;
        }

        public SpritePoseHistory(SpriteRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                throw new ArgumentException("At least one borrowed renderer is required.", nameof(renderers));
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) throw new ArgumentException("Renderers must be alive.", nameof(renderers));
                for (int j = 0; j < i; j++)
                    if (renderers[j] == renderers[i])
                        throw new ArgumentException("Renderers must be distinct.", nameof(renderers));
            }
            targets = (SpriteRenderer[])renderers.Clone();
            entries = new Entry[targets.Length];
            Clear();
        }

        public int Capacity => entries.Length;
        public int ActiveCount { get; private set; }

        /// <summary>Capture is atomic on failure. A full history rejects unless replacement is explicit.</summary>
        public bool TryCapture(SpriteRenderer source, float duration, Color tint,
            bool replaceOldest = false, int sortingOrderOffset = 0, Vector3? worldPosition = null)
        {
            if (!source || !source.sprite || source.drawMode != SpriteDrawMode.Simple ||
                !Finite(duration) || duration <= 0f || !Finite(tint)) return false;
            var position = worldPosition ?? source.transform.position;
            var scale = source.transform.lossyScale;
            var rotation = source.transform.rotation;
            var color = source.color * tint;
            long order = (long)source.sortingOrder + sortingOrderOffset;
            if (!Finite(position) || !Finite(scale) || !Finite(rotation) || !Finite(color) ||
                order < short.MinValue || order > short.MaxValue) return false;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] == source) return false;
            int slot = FindSlot(replaceOldest);
            if (slot < 0) return false;
            if (!entries[slot].Active) ActiveCount++;
            entries[slot] = new Entry
            {
                Active = true, Duration = duration, Sprite = source.sprite,
                Position = position, Scale = scale, Rotation = rotation,
                FlipX = source.flipX, FlipY = source.flipY,
                SortingLayer = source.sortingLayerID, SortingOrder = (int)order, Color = color
            };
            next = (slot + 1) % entries.Length;
            Draw(slot, 1f);
            return true;
        }

        /// <summary>Invalid or negative delta freezes age; intensity affects visibility, not lifetime.</summary>
        public bool Advance(float delta, float intensity = 1f)
        {
            if (!Finite(delta) || delta < 0f) delta = 0f;
            intensity = Finite(intensity) ? Mathf.Clamp01(intensity) : 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                if (!entries[i].Active) continue;
                entries[i].Age += delta;
                if (!targets[i] || entries[i].Age >= entries[i].Duration)
                    Release(i);
                else
                    Draw(i, intensity);
            }
            return ActiveCount > 0;
        }

        public void Clear()
        {
            for (int i = 0; i < entries.Length; i++) Release(i);
            next = 0;
        }

        private int FindSlot(bool replaceOldest)
        {
            int oldest = -1;
            for (int offset = 0; offset < entries.Length; offset++)
            {
                int i = (next + offset) % entries.Length;
                if (!targets[i]) continue;
                if (!entries[i].Active) return i;
                if (oldest < 0 || entries[i].Age > entries[oldest].Age) oldest = i;
            }
            return replaceOldest ? oldest : -1;
        }

        private void Draw(int index, float intensity)
        {
            var target = targets[index];
            var pose = entries[index];
            target.enabled = intensity > 0.001f;
            target.sprite = pose.Sprite;
            target.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            var parentScale = target.transform.parent ? target.transform.parent.lossyScale : Vector3.one;
            target.transform.localScale = new Vector3(Divide(pose.Scale.x, parentScale.x),
                Divide(pose.Scale.y, parentScale.y), Divide(pose.Scale.z, parentScale.z));
            target.flipX = pose.FlipX;
            target.flipY = pose.FlipY;
            target.sortingLayerID = pose.SortingLayer;
            target.sortingOrder = pose.SortingOrder;
            var color = pose.Color;
            color.a *= intensity * Mathf.Clamp01(1f - pose.Age / pose.Duration);
            target.color = color;
        }

        private void Release(int index)
        {
            if (entries[index].Active) ActiveCount--;
            entries[index] = default;
            if (!targets[index]) return;
            targets[index].enabled = false;
            targets[index].sprite = null;
        }

        private static float Divide(float value, float divisor) => Mathf.Abs(divisor) > 0.0001f ? value / divisor : 0f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
        private static bool Finite(Color value) => Finite(value.r) && Finite(value.g) && Finite(value.b) && Finite(value.a);
    }
}
