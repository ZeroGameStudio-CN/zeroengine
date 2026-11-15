using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render.Attachments
{
    public static class SurfaceAttachmentPlacement
    {
        private const float Epsilon = 0.0005f;
        private const float MinimumSpan = 0.025f;
        private const int MaxVertices = 8192;
        private const int MaxIndices = 24576;

        // Shared scratch for the synchronous, main-thread impact. No references to targets survive a hit.
        private static readonly List<Vector3> MeshVertices = new();
        private static readonly List<int> MeshSubmesh = new();
        private static readonly List<int> Indices = new();
        private static readonly List<Vector2> Projected = new();
        private static readonly List<Vector2> Uvs = new();
        private static readonly List<Vector2> Intervals = new();

        public static Vector3 Resolve(Renderer renderer, Collider2D hurtbox, Vector3 hit, Vector2 approach,
            float spread, float tipReach)
        {
            if (spread <= 0f || !renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                !hurtbox || !hurtbox.isActiveAndEnabled || approach.sqrMagnitude < Epsilon) return hit;

            Vector2 axis = approach.normalized;
            var across = new Vector2(-axis.y, axis.x);
            if (!ProjectGeometry(renderer, hit, axis, across)) return hit;

            // Compact parts can use their middle; a large enemy cannot bury most of the shaft.
            float travel = Mathf.Clamp(tipReach * 1.65f, 0.35f, 1.35f) * Mathf.Clamp01(spread);
            if (TrySlice(hurtbox, hit, axis, across, 0f, travel, out var anchor)) return anchor;

            // A slightly offset probe can find a nearby edge on irregular shapes. Never search the whole body.
            for (int step = 1; step <= 2; step++)
            {
                float offset = travel * (0.16f * step);
                bool above = TrySlice(hurtbox, hit, axis, across, offset, travel, out var a);
                bool below = TrySlice(hurtbox, hit, axis, across, -offset, travel, out var b);
                if (above && below) return Vector2.Dot(a - hit, axis) <= Vector2.Dot(b - hit, axis) ? a : b;
                if (above || below) return above ? a : b;
            }
            return hit;
        }

        private static bool ProjectGeometry(Renderer renderer, Vector3 hit, Vector2 axis, Vector2 across, bool sampleAlpha = false)
        {
            Indices.Clear();
            Projected.Clear();
            Uvs.Clear();
            var matrix = renderer.transform.localToWorldMatrix;
            if (renderer is SpriteRenderer spriteRenderer)
            {
                var sprite = spriteRenderer.sprite;
                if (!sprite || spriteRenderer.color.a <= 0f || spriteRenderer.drawMode != SpriteDrawMode.Simple) return false;
                var vertices = sprite.vertices;
                // Full rectangular sprite geometry cannot tell opaque pixels from transparent padding.
                if (vertices.Length < 3 || vertices.Length > MaxVertices || (!sampleAlpha && IsRectangle(vertices))) return false;
                var triangles = sprite.triangles;
                if (triangles.Length > MaxIndices) return false;
                foreach (var vertex in vertices)
                {
                    var local = new Vector3(spriteRenderer.flipX ? -vertex.x : vertex.x, spriteRenderer.flipY ? -vertex.y : vertex.y);
                    Projected.Add(Project(matrix.MultiplyPoint3x4(local) - hit, axis, across));
                }
                if (sampleAlpha) Uvs.AddRange(sprite.uv);
                foreach (int index in triangles) Indices.Add(index);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                var mesh = filter.sharedMesh;
                if (!mesh || !mesh.isReadable || mesh.vertexCount < 3 || mesh.vertexCount > MaxVertices ||
                    mesh.subMeshCount > 32) return false;
                int count = 0;
                for (int part = 0; part < mesh.subMeshCount; part++)
                {
                    if (mesh.GetTopology(part) != MeshTopology.Triangles) continue;
                    uint partCount = mesh.GetIndexCount(part);
                    if (partCount > (uint)(MaxIndices - count)) return false;
                    count += (int)partCount;
                }
                if (count < 3) return false;
                mesh.GetVertices(MeshVertices);
                if (sampleAlpha) mesh.GetUVs(0, Uvs);
                foreach (var vertex in MeshVertices)
                    Projected.Add(Project(matrix.MultiplyPoint3x4(vertex) - hit, axis, across));
                for (int part = 0; part < mesh.subMeshCount; part++)
                {
                    if (mesh.GetTopology(part) != MeshTopology.Triangles) continue;
                    mesh.GetTriangles(MeshSubmesh, part);
                    Indices.AddRange(MeshSubmesh);
                }
            }
            return Indices.Count >= 3;
        }

        // Coverage follows the hit region's local axes, including mirroring and
        // nonuniform scale. Reach is supplied by the arrow, not a monster profile.
        public static bool TryFindVisibleContact(Renderer renderer, Collider2D hurtbox, Vector3 endpoint,
            Vector2 forward, out Vector2 shift, Vector2 preferred = default, Vector2 anchor = default, float reach = 1.15f, float minimumExposed = 0f)
        {
            shift = default;
            if (!renderer || !hurtbox || !hurtbox.isActiveAndEnabled || forward.sqrMagnitude < Epsilon ||
                !float.IsFinite(reach) || reach <= Epsilon || !float.IsFinite(minimumExposed) || minimumExposed < 0f) return false;
            var texture = renderer is SpriteRenderer sprite && sprite.sprite
                ? sprite.sprite.texture : AttachmentSurfaceCoverage.MeshTexture(renderer);
            var alpha = AttachmentSurfaceCoverage.GetAlpha(texture);
            if (alpha == null) return false;
            Vector2 axis = forward.normalized;
            Vector2 across = new(-axis.y, axis.x);
            if (!ProjectGeometry(renderer, endpoint, axis, across, sampleAlpha: true)) return false;

            Vector2 spanX, spanY, patchX, patchY;
            if (hurtbox is BoxCollider2D box)
            {
                spanX = box.transform.TransformVector(Vector3.right * box.size.x);
                spanY = box.transform.TransformVector(Vector3.up * box.size.y);
                float radius = Mathf.Min(box.size.x, box.size.y) * .12f;
                patchX = box.transform.TransformVector(Vector3.right * radius);
                patchY = box.transform.TransformVector(Vector3.up * radius);
            }
            else
            {
                var size = hurtbox.bounds.size;
                spanX = Vector2.right * size.x;
                spanY = Vector2.up * size.y;
                float radius = Mathf.Min(size.x, size.y) * .12f;
                patchX = Vector2.right * radius;
                patchY = Vector2.up * radius;
            }
            if (spanX.sqrMagnitude < Epsilon * Epsilon || spanY.sqrMagnitude < Epsilon * Epsilon) return false;
            // A large body must not demand more buried shaft than this arrow has.
            patchX = Vector2.ClampMagnitude(patchX, reach * .21f);
            patchY = Vector2.ClampMagnitude(patchY, reach * .21f);
            bool Try(Vector2 delta)
            {
                var local = Project(delta, axis, across);
                if (local.x < -reach || local.x > reach || Mathf.Abs(local.y) > reach * .5f) return false;
                // Physics already owns the hit. Visual entry follows this renderer's surface,
                // which can extend beyond the damage collider on a large character.
                if (!ContainsProjectedPoint(local, texture, alpha)) return false;
                if (minimumExposed > 0f && ContainsProjectedPoint(local - new Vector2(minimumExposed, 0f), texture, alpha)) return false;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * .25f;
                    var offset = patchX * Mathf.Cos(angle) + patchY * Mathf.Sin(angle);
                    if (!ContainsProjectedPoint(local + Project(offset, axis, across), texture, alpha)) return false;
                }
                return true;
            }
            if (Try(preferred)) { shift = preferred; return true; }
            if (Try(Vector2.zero)) return true;
            // The authoritative hit remains a useful candidate even when cropping
            // leaves the visible shaft well outside a very small target.
            if (Try(anchor)) { shift = anchor; return true; }
            // Prefer withdrawing along the same shaft before moving sideways.
            for (int i = 1; i <= 16; i++)
            {
                var back = -axis * (reach * i / 16f);
                if (Try(back)) { shift = back; return true; }
            }
            Vector2 stepX = spanX / 24f, stepY = spanY / 24f;
            for (int ring = 1; ring <= 12; ring++)
            {
                for (int y = -ring; y <= ring; y++)
                {
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != ring) continue;
                        var candidate = anchor + stepX * x + stepY * y;
                        if (Try(candidate)) { shift = candidate; return true; }
                    }
                }
            }
            return false;
        }

        private static bool ContainsProjectedPoint(Vector2 point, Texture2D texture, byte[] alpha)
        {
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                int i0 = Indices[i], i1 = Indices[i + 1], i2 = Indices[i + 2];
                Vector2 a = Projected[i0], b = Projected[i1] - a, c = Projected[i2] - a, p = point - a;
                float area = b.x * c.y - b.y * c.x;
                if (Mathf.Abs(area) < Epsilon * Epsilon) continue;
                float t = (p.x * c.y - p.y * c.x) / area;
                float u = (b.x * p.y - b.y * p.x) / area;
                if (t < 0f || u < 0f || t + u > 1f) continue;
                if (alpha == null) return true;
                if (Uvs.Count != Projected.Count) return false;
                var uv = Uvs[i0] + (Uvs[i1] - Uvs[i0]) * t + (Uvs[i2] - Uvs[i0]) * u;
                if (AttachmentSurfaceCoverage.IsOpaque(alpha, texture.width, texture.height, uv)) return true;
            }
            return false;
        }

        private static Vector2 Project(Vector3 delta, Vector2 axis, Vector2 across) =>
            new(Vector2.Dot(delta, axis), Vector2.Dot(delta, across));

        private static bool TrySlice(Collider2D hurtbox, Vector3 hit, Vector2 axis, Vector2 across,
            float offset, float travel, out Vector3 anchor)
        {
            anchor = hit;
            Vector3 origin = hit + (Vector3)(across * offset);
            float entry = 0f, exit = float.PositiveInfinity;
            if (!ClipToHurtbox(hurtbox, origin, axis, ref entry, ref exit) || entry > travel) return false;

            Intervals.Clear();
            for (int i = 0; i + 2 < Indices.Count; i += 3)
            {
                var a = Projected[Indices[i]];
                var b = Projected[Indices[i + 1]];
                var c = Projected[Indices[i + 2]];
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < Epsilon * Epsilon) continue;
                float start = float.PositiveInfinity, end = float.NegativeInfinity;
                SliceEdge(a, b, offset, ref start, ref end);
                SliceEdge(b, c, offset, ref start, ref end);
                SliceEdge(c, a, offset, ref start, ref end);
                start = Mathf.Max(entry, start);
                end = Mathf.Min(exit, end);
                if (start < travel && end > start) Intervals.Add(new Vector2(start, end));
            }

            if (Intervals.Count == 0) return false;
            Intervals.Sort(CompareStart);
            Vector2 run = Intervals[0];
            int next = 1;
            while (next < Intervals.Count && Intervals[next].x <= run.y + Epsilon)
                run.y = Mathf.Max(run.y, Intervals[next++].y);
            // Only the first continuous part is eligible: don't tunnel through an empty gap to a deeper limb.
            float reachable = Mathf.Min(run.y, travel) - run.x;
            if (reachable < MinimumSpan) return false;
            float inset = Mathf.Min((run.y - run.x) * 0.5f, Mathf.Min(reachable * 0.8f, travel * 0.60f));
            var point = origin + (Vector3)(axis * (run.x + inset));
            if (!hurtbox.OverlapPoint(point)) return false;
            anchor = point;
            return true;
        }

        private static int CompareStart(Vector2 a, Vector2 b) => a.x.CompareTo(b.x);

        private static void SliceEdge(Vector2 a, Vector2 b, float row, ref float start, ref float end)
        {
            if (row < Mathf.Min(a.y, b.y) || row > Mathf.Max(a.y, b.y)) return;
            if (Mathf.Abs(b.y - a.y) <= Epsilon)
            {
                start = Mathf.Min(start, Mathf.Min(a.x, b.x));
                end = Mathf.Max(end, Mathf.Max(a.x, b.x));
                return;
            }
            float x = Mathf.Lerp(a.x, b.x, (row - a.y) / (b.y - a.y));
            start = Mathf.Min(start, x);
            end = Mathf.Max(end, x);
        }

        private static bool ClipToHurtbox(Collider2D hurtbox, Vector3 origin, Vector2 axis, ref float entry, ref float exit)
        {
            Vector2 center, half;
            if (hurtbox is BoxCollider2D box)
            {
                // Use the actual box axes even when this body part has been rotated or mirrored.
                origin = box.transform.InverseTransformPoint(origin);
                axis = box.transform.InverseTransformVector(axis);
                center = box.offset;
                half = box.size * 0.5f;
            }
            else
            {
                var bounds = hurtbox.bounds;
                center = bounds.center;
                half = bounds.extents;
            }
            Vector2 delta = (Vector2)origin - center;
            return half.x > 0f && half.y > 0f &&
                ClipAxis(delta.x, axis.x, half.x, ref entry, ref exit) &&
                ClipAxis(delta.y, axis.y, half.y, ref entry, ref exit);
        }

        private static bool ClipAxis(float origin, float velocity, float half, ref float entry, ref float exit)
        {
            if (Mathf.Abs(velocity) < Epsilon) return Mathf.Abs(origin) <= half;
            float a = (-half - origin) / velocity, b = (half - origin) / velocity;
            entry = Mathf.Max(entry, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return exit > entry;
        }

        private static bool IsRectangle(Vector2[] vertices)
        {
            if (vertices.Length != 4) return false;
            var min = vertices[0]; var max = vertices[0];
            foreach (var vertex in vertices) { min = Vector2.Min(min, vertex); max = Vector2.Max(max, vertex); }
            foreach (var vertex in vertices)
                if ((!Mathf.Approximately(vertex.x, min.x) && !Mathf.Approximately(vertex.x, max.x)) ||
                    (!Mathf.Approximately(vertex.y, min.y) && !Mathf.Approximately(vertex.y, max.y))) return false;
            return true;
        }
    }
}
