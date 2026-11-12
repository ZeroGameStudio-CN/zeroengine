using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.FogOfWar.Unity
{
    // Current sight is geometric; the exploration grid is only a persistent sampling of it.
    public class SightPolygon2D : IVisibilityQuery2D
    {
        public readonly struct Segment
        {
            public readonly Vector2 A, B;
            public Segment(Vector2 a, Vector2 b) { A = a; B = b; }
        }

        private static readonly Comparison<float> CompareAngles = (left, right) => left.CompareTo(right);
        // Pure immutable math data; initialized only when the prepared path is requested.
        private static class UniformRing
        {
            internal static readonly float[] Angles = new float[128];
            internal static readonly Vector2[] Directions = new Vector2[128];
            static UniformRing()
            {
                for (int i = 0; i < 128; i++)
                {
                    float angle = i * (Mathf.PI * 2 / 128);
                    Angles[i] = angle;
                    Directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
            }
        }
        private readonly List<float> angles = new();
        private readonly List<float> orderedAngles = new();
        private readonly HashSet<Vector2> endpoints = new();
        [UnityEngine.Scripting.Preserve]
        public bool EndpointDeduplicationEnabled { get; set; } = true;
        private int uniformAngleCount;
        [UnityEngine.Scripting.Preserve]
        public int InputAngleCount => angles.Count + uniformAngleCount;
        public readonly List<Vector2> Points = new();
        public Vector2 Origin { get; private set; }
        public Vector2 Radius { get; private set; }
        public float Forward { get; private set; }
        public float Facing { get; private set; }
        public uint Revision { get; private set; }
        private float reach;
        [UnityEngine.Scripting.Preserve]
        public bool PreparedIntersectionsEnabled { get; set; }
        private RaySegment[] preparedSegments = Array.Empty<RaySegment>();

        private readonly struct RaySegment
        {
            public readonly Vector2 Edge, Offset;
            public readonly float DistanceNumerator;
            public RaySegment(Vector2 origin, Segment segment)
            {
                var edge = segment.B - segment.A;
                var offset = segment.A - origin;
                Edge = edge;
                Offset = offset;
                DistanceNumerator = Cross(offset, edge);
            }
        }
        private const int CoarseSectorCount = 32, FineSectorCount = 128;
        private readonly List<int>[] coarseSectors = CreateSectors(CoarseSectorCount);
        private List<int>[] fineSectors, sectorSegments;
        private float sectorScale;
        private int sectorMask;
        private int SectorCount => sectorSegments.Length;
        public int CandidateChecks { get; private set; }
        [UnityEngine.Scripting.Preserve]
        public int UniformDirectionReuses { get; private set; }
        public int SectorEntries { get; private set; }
        private int inputSegments;
        public SightPolygon2D()
        { sectorSegments = coarseSectors; sectorScale = CoarseSectorCount / (Mathf.PI * 2); sectorMask = CoarseSectorCount - 1; FineSectorsEnabled = true; }
        // Narrow only the conservative angular broad phase. Every ray, intersection,
        // tolerance and ordered result remains the existing exact calculation.
        [UnityEngine.Scripting.Preserve]
        public bool FineSectorsEnabled
        {
            get => SectorCount == FineSectorCount;
            set
            {
                if (value == FineSectorsEnabled) return;
                if (value) fineSectors ??= CreateSectors(FineSectorCount);
                sectorSegments = value ? fineSectors : coarseSectors;
                sectorScale = SectorCount / (Mathf.PI * 2);
                sectorMask = SectorCount - 1;
            }
        }
        [Serializable] private struct SectorDiagnostics
        { public int sectors, segments, rays, candidateChecks, indexEntries; }
        [UnityEngine.Scripting.Preserve]
        public string DescribeSectorDiagnostics() => JsonUtility.ToJson(new SectorDiagnostics
        { sectors = SectorCount, segments = inputSegments, rays = orderedAngles.Count,
            candidateChecks = CandidateChecks, indexEntries = SectorEntries });

        private static List<int>[] CreateSectors(int count)
        {
            var result = new List<int>[count];
            for (int i = 0; i < result.Length; i++) result[i] = new List<int>();
            return result;
        }

        private void IndexSegment(Segment segment, int index)
        {
            // Match Intersect's along-segment tolerance before computing angular extent.
            var edge = segment.B - segment.A;
            var a = segment.A - Origin - edge * .000001f;
            var b = segment.B - Origin + edge * .000001f;
            var expandedEdge = b - a;
            float t = Mathf.Clamp01(-Vector2.Dot(a, expandedEdge) / Mathf.Max(.0000001f, expandedEdge.sqrMagnitude));
            if ((a + expandedEdge * t).sqrMagnitude < .00000001f)
            {
                SectorEntries += SectorCount;
                for (int i = 0; i < SectorCount; i++) sectorSegments[i].Add(index);
                return;
            }
            float start = Normalize(Mathf.Atan2(a.y, a.x));
            float finish = Normalize(Mathf.Atan2(b.y, b.x));
            float span = Normalize(finish - start);
            if (span > Mathf.PI) { start = finish; span = Mathf.PI * 2 - span; }
            int first = Mathf.FloorToInt(start * sectorScale) - 1;
            int last = Mathf.FloorToInt((start + span) * sectorScale) + 1;
            SectorEntries += last - first + 1;
            for (int i = first; i <= last; i++) sectorSegments[i & sectorMask].Add(index);
        }

        // Keep an authored eye offset on the player's side of solid geometry.
        // The anchor is the physical body's center, rather than its feet/head.
        public static Vector2 ConstrainOrigin(Vector2 anchor, Vector2 desired, IReadOnlyList<Segment> segments)
        {
            var delta = desired - anchor;
            float distance = delta.magnitude;
            if (distance < .0001f) return anchor;
            var direction = delta / distance;
            float allowed = distance;
            for (int i = 0; i < segments.Count; i++)
                if (Intersect(anchor, direction, segments[i], out float hit) && hit <= distance)
                    allowed = Mathf.Min(allowed, Mathf.Max(0, hit - .03f));
            return anchor + direction * allowed;
        }

        public static Vector2 FollowOrigin(Vector2 previous, Vector2 desired, Vector2 movementStep,
            float elapsed, float smoothTime, float teleportDistance)
        {
            // Compare actual movement, independently of the follower's accumulated lag.
            // Invalid authoring falls back to direct positioning rather than NaN geometry.
            if (!(smoothTime > 0) || float.IsInfinity(smoothTime)
                || !(teleportDistance > 0) || float.IsInfinity(teleportDistance)
                || float.IsNaN(elapsed) || float.IsInfinity(elapsed)
                || movementStep.sqrMagnitude > teleportDistance * teleportDistance) return desired;
            return Vector2.Lerp(previous, desired, 1 - Mathf.Exp(-Mathf.Max(0, elapsed) / smoothTime));
        }

        public void Rebuild(Vector2 origin, Vector2 radius, float facing, float forward,
            IReadOnlyList<Segment> segments)
        {
            CandidateChecks = SectorEntries = UniformDirectionReuses = 0; inputSegments = segments.Count;
            unchecked { Revision++; }
            Origin = origin;
            Radius = new Vector2(Mathf.Max(.25f, radius.x), Mathf.Max(.25f, radius.y));
            Facing = Mathf.Clamp(facing, -1, 1);
            Forward = Mathf.Max(0, forward);
            reach = Mathf.Max(Radius.x + Forward, Radius.y) + 2;
            bool prepare = PreparedIntersectionsEnabled;
            uniformAngleCount = prepare ? 128 : 0;
            if (prepare && preparedSegments.Length < segments.Count)
                Array.Resize(ref preparedSegments, Math.Max(segments.Count, preparedSegments.Length * 2));
            angles.Clear(); orderedAngles.Clear(); Points.Clear(); endpoints.Clear();
            for (int i = 0; i < SectorCount; i++) sectorSegments[i].Clear();
            // Uniform samples bound the distant ring; wall endpoints supply exact silhouettes.
            if (!prepare) for (int i = 0; i < 128; i++) angles.Add(i * (Mathf.PI * 2 / 128));
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                AddEndpoint(segment.A); AddEndpoint(segment.B);
                IndexSegment(segment, i);
                if (prepare) preparedSegments[i] = new RaySegment(origin, segment);
            }
            angles.Sort(CompareAngles);
            if (prepare) { RebuildPrepared(origin); return; }
            foreach (float angle in angles)
            {
                if (orderedAngles.Count > 0 && angle - orderedAngles[orderedAngles.Count - 1] < .000001f) continue;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = reach;
                var candidates = sectorSegments[Mathf.Min(SectorCount - 1, (int)(angle * sectorScale))];
                CandidateChecks += candidates.Count;
                for (int i = 0; i < candidates.Count; i++)
                    if (Intersect(origin, direction, segments[candidates[i]], out float hit)) distance = Mathf.Min(distance, hit);
                orderedAngles.Add(angle);
                Points.Add(origin + direction * distance);
            }
        }

        private void RebuildPrepared(Vector2 origin)
        {
            int endpoint = 0, uniform = 0;
            while (endpoint < angles.Count || uniform < 128)
            {
                bool fromRing = uniform < 128
                    && (endpoint == angles.Count || UniformRing.Angles[uniform] <= angles[endpoint]);
                float angle = fromRing ? UniformRing.Angles[uniform++] : angles[endpoint++];
                if (orderedAngles.Count > 0 && angle - orderedAngles[orderedAngles.Count - 1] < .000001f) continue;
                var direction = fromRing ? UniformRing.Directions[uniform - 1]
                    : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (fromRing) UniformDirectionReuses++;
                float distance = reach;
                var candidates = sectorSegments[Mathf.Min(SectorCount - 1, (int)(angle * sectorScale))];
                CandidateChecks += candidates.Count;
                for (int i = 0; i < candidates.Count; i++)
                    if (IntersectPrepared(direction, in preparedSegments[candidates[i]], out float hit))
                        distance = Mathf.Min(distance, hit);
                orderedAngles.Add(angle);
                Points.Add(origin + direction * distance);
            }
        }

        private void AddEndpoint(Vector2 point)
        {
            var delta = point - Origin;
            if (delta.sqrMagnitude < .0000001f) return;
            // Shared wall vertices produce identical angular samples. Keep exact positions:
            // nearby endpoints at narrow corners must still contribute independently.
            if (EndpointDeduplicationEnabled && !endpoints.Add(point)) return;
            float angle = Mathf.Atan2(delta.y, delta.x);
            // Perturb by a world-space fraction, keeping corner gaps below a screen pixel.
            float epsilon = Mathf.Clamp(.0005f / delta.magnitude, .000002f, .0001f);
            angles.Add(Normalize(angle - epsilon));
            angles.Add(Normalize(angle));
            angles.Add(Normalize(angle + epsilon));
        }

        public bool IsVisible(Vector2 position) => Sample(position) >= .5f;
        public float Sample(Vector2 position)
        {
            if (Points.Count < 3) return 0;
            var delta = position - Origin;
            float horizontal = Radius.x + Forward * Mathf.Max(0, Mathf.Sign(delta.x) * Facing);
            if (new Vector2(delta.x / horizontal, delta.y / Radius.y).sqrMagnitude > 1) return 0;
            if (delta.sqrMagnitude < .000001f) return 1;
            float angle = Normalize(Mathf.Atan2(delta.y, delta.x));
            int index = orderedAngles.BinarySearch(angle);
            if (index >= 0) return delta.sqrMagnitude <= (Points[index] - Origin).sqrMagnitude + .000001f ? 1 : 0;
            index = ~index;
            int previous = (index + Points.Count - 1) % Points.Count;
            int next = index % Points.Count;
            var segment = new Segment(Points[previous], Points[next]);
            return Intersect(Origin, delta.normalized, segment, out float distance)
                && delta.magnitude <= distance + .0001f ? 1 : 0;
        }

        private static bool IntersectPrepared(Vector2 direction, in RaySegment segment, out float distance)
        {
            float denominator = Cross(direction, segment.Edge);
            distance = 0;
            if (Mathf.Abs(denominator) < .0000001f) return false;
            float along = Cross(segment.Offset, direction) / denominator;
            if (!(along >= -.000001f && along <= 1.000001f)) return false;
            distance = segment.DistanceNumerator / denominator;
            return distance >= 0;
        }

        private static float Normalize(float angle) => Mathf.Repeat(angle, Mathf.PI * 2);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        public static bool Intersect(Vector2 origin, Vector2 direction, Segment segment, out float distance)
        {
            var edge = segment.B - segment.A;
            float denominator = Cross(direction, edge);
            distance = 0;
            if (Mathf.Abs(denominator) < .0000001f) return false;
            var offset = segment.A - origin;
            float along = Cross(offset, direction) / denominator;
            distance = Cross(offset, edge) / denominator;
            return distance >= 0 && along >= -.000001f && along <= 1.000001f;
        }
    }
}
