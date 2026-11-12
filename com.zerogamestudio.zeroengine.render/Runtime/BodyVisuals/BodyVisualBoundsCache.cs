using System;
using UnityEngine;

namespace ZeroEngine.Render
{
    /// <summary>
    /// Reuses the native world centre only within one render pass and only for
    /// host-declared ordinary sprites. Eligible renderers must not use custom
    /// world bounds, skinning or a deformable buffer. Other sources retain native reads.
    /// </summary>
    public sealed class BodyVisualBoundsCache
    {
        private readonly struct Stamp : IEquatable<Stamp>
        {
            private readonly Sprite sprite;
            private readonly Bounds local, geometry;
            private readonly Matrix4x4 matrix;
            private readonly Vector2 size;
            private readonly bool flipX, flipY;
            private readonly SpriteDrawMode drawMode;

            public Stamp(SpriteRenderer renderer)
            {
                sprite = renderer.sprite;
                geometry = sprite ? sprite.bounds : default;
                local = renderer.localBounds;
                matrix = renderer.localToWorldMatrix;
                size = renderer.size;
                flipX = renderer.flipX;
                flipY = renderer.flipY;
                drawMode = renderer.drawMode;
            }

            public bool Equals(Stamp other) => ReferenceEquals(sprite, other.sprite) &&
                local.Equals(other.local) && geometry.Equals(other.geometry) &&
                matrix.Equals(other.matrix) && size.Equals(other.size) &&
                flipX == other.flipX && flipY == other.flipY && drawMode == other.drawMode;

            public bool IsFinite
            {
                get
                {
                    if (!Finite(local.center) || !Finite(local.extents) || !Finite(geometry.center) ||
                        !Finite(geometry.extents) || !Finite(size.x) || !Finite(size.y)) return false;
                    for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) return false;
                    return true;
                }
            }
        }

        private Func<Renderer, bool> eligible;
        private Renderer previous;
        private Stamp stamp;
        private Vector3 center;
        private bool valid;
        public bool Enabled => eligible != null;
        public int NativeReadsThisPass { get; private set; }

        public void SetEligibility(Func<Renderer, bool> predicate)
        {
            eligible = predicate;
            BeginPass();
        }

        public void BeginPass()
        {
            valid = false;
            previous = null;
            stamp = default;
            NativeReadsThisPass = 0;
        }

        public Vector3 GetCenter(Renderer renderer)
        {
            if (renderer is not SpriteRenderer sprite || eligible == null || !eligible(renderer) || !sprite.sprite)
            {
                valid = false;
                previous = null;
                NativeReadsThisPass++;
                return renderer.bounds.center;
            }
            var before = new Stamp(sprite);
            if (valid && ReferenceEquals(previous, renderer) && stamp.Equals(before)) return center;
            valid = false;
            NativeReadsThisPass++;
            var result = renderer.bounds.center;
            var after = new Stamp(sprite);
            // Completing renderer jobs can run callbacks. Do not retain a result if
            // its inputs changed during the native read. Comparisons are exact, not epsilon based.
            if (before.Equals(after) && after.IsFinite && Finite(result))
            {
                previous = renderer;
                stamp = after;
                center = result;
                valid = true;
            }
            return result;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
