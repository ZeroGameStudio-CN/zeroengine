using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>Collision layers, optional room boundary and shapes to ignore at the destination.</summary>
    public readonly struct TeleportLandingOptions2D
    {
        public TeleportLandingOptions2D(LayerMask blockingMask, bool ignoreTriggers = true,
            PolygonCollider2D boundary = null, LayerMask triggerBlockingMask = default,
            Transform ignoredRoot = null, LayerMask ignoredLayers = default)
        {
            BlockingMask = blockingMask;
            IgnoreTriggers = ignoreTriggers;
            Boundary = boundary;
            TriggerBlockingMask = triggerBlockingMask;
            IgnoredRoot = ignoredRoot;
            IgnoredLayers = ignoredLayers;
        }

        public LayerMask BlockingMask { get; }
        public bool IgnoreTriggers { get; }
        public PolygonCollider2D Boundary { get; }
        public LayerMask TriggerBlockingMask { get; }
        public Transform IgnoredRoot { get; }
        public LayerMask IgnoredLayers { get; }

        internal LayerMask QueryBlockingMask => BlockingMask.value & ~IgnoredLayers.value;

        internal bool IsIgnored(Collider2D collider)
        {
            if ((IgnoredLayers.value & (1 << collider.gameObject.layer)) != 0) return true;
            return IgnoredRoot && (collider.transform == IgnoredRoot || collider.transform.IsChildOf(IgnoredRoot));
        }
    }

    /// <summary>Call-site policy for a bounded search around a preferred root position.</summary>
    public readonly struct TeleportLandingSearchPolicy
    {
        public TeleportLandingSearchPolicy(float maximumDistance, int maximumCandidates = 96, float clearance = .02f,
            Vector2 halfPlaneNormal = default, float minimumProjection = 0f, LayerMask downwardFloorMask = default,
            LayerMask terrainMask = default, bool requiresLineOfSight = false)
        {
            MaximumDistance = maximumDistance;
            MaximumCandidates = maximumCandidates;
            Clearance = clearance;
            HalfPlaneNormal = halfPlaneNormal;
            MinimumProjection = minimumProjection;
            DownwardFloorMask = downwardFloorMask;
            TerrainMask = terrainMask;
            RequiresLineOfSight = requiresLineOfSight;
        }

        public float MaximumDistance { get; }
        public int MaximumCandidates { get; }
        public float Clearance { get; }
        // Optional caller region: Dot(normal, rootPosition) >= minimumProjection. Zero disables it.
        public Vector2 HalfPlaneNormal { get; }
        public float MinimumProjection { get; }
        /// <summary>Floor layers that can veto downward travel from the current body position.</summary>
        public LayerMask DownwardFloorMask { get; }
        /// <summary>Layers that bound corrections to the requested space.</summary>
        public LayerMask TerrainMask { get; }
        /// <summary>Also requires an unobstructed route from the current position to the destination.</summary>
        public bool RequiresLineOfSight { get; }
    }

    public readonly struct TeleportLandingSearchReport
    {
        internal TeleportLandingSearchReport(int candidates, int queries, bool limited)
        {
            CandidatesTested = candidates;
            DistanceQueries = queries;
            BudgetExhausted = limited;
        }

        public int CandidatesTested { get; }
        public int DistanceQueries { get; }
        public bool BudgetExhausted { get; }
    }

    /// <summary>Main-thread placement queries for an existing 2D physics body.</summary>
    public static class TeleportLanding2D
    {
        // Synchronous main-thread scratch. Candidate order is independent of Unity's random state.
        private const int LandingCandidateCapacity = 256;
        private const int LandingOverlapCapacity = 128;
        private static readonly Vector2[] LandingCandidates = new Vector2[LandingCandidateCapacity];
        private static readonly float[] LandingCandidateCosts = new float[LandingCandidateCapacity];
        private static readonly Collider2D[] SearchOverlaps = new Collider2D[LandingOverlapCapacity];
        private static readonly RaycastHit2D[] PathHits = new RaycastHit2D[LandingOverlapCapacity];
        private static readonly Collider2D[] LandingOverlaps = new Collider2D[64];
        private static readonly List<Bounds> BodyShapeBounds = new(4);
        private static readonly List<Vector2> BoundaryPathPoints = new(8);
        private static readonly PhysicsShapeGroup2D LandingProbeShapes = new(4, 16);

        /// <summary>
        /// Finds a nearby root position that fits the live collider in its physics scene.
        /// The search restores the actor's original pose. The caller supplies the requested point,
        /// the search budget and any terrain or room constraints.
        /// </summary>
        public static bool TryFind(Transform root, Collider2D body, Vector2 preferred,
            TeleportLandingOptions2D options, TeleportLandingSearchPolicy policy,
            out Vector2 safePosition, out TeleportLandingSearchReport report)
        {
            safePosition = preferred;
            report = default;
            if (!root || !body || !body.isActiveAndEnabled || options.BlockingMask.value == 0 ||
                body.transform != root && !body.transform.IsChildOf(root) ||
                body.attachedRigidbody && !body.attachedRigidbody.simulated ||
                !IsFinite(preferred.x) || !IsFinite(preferred.y) ||
                !IsFinite(policy.MaximumDistance) || policy.MaximumDistance < 0f ||
                !IsFinite(policy.Clearance) || policy.Clearance < 0f ||
                !IsFinite(policy.HalfPlaneNormal.x) || !IsFinite(policy.HalfPlaneNormal.y) ||
                !IsFinite(policy.HalfPlaneNormal.sqrMagnitude) || !IsFinite(policy.MinimumProjection) ||
                policy.MaximumCandidates < 1 || policy.MaximumCandidates > LandingCandidateCapacity ||
                policy.RequiresLineOfSight && policy.TerrainMask.value == 0)
                return false;

            Scene bodyScene = body.gameObject.scene;
            if (!bodyScene.IsValid() || !bodyScene.isLoaded) return false;
            PhysicsScene2D physicsScene = bodyScene.GetPhysicsScene2D();
            if (!physicsScene.IsValid()) return false;

            Physics2D.SyncTransforms();
            Bounds originalBounds = GetBodyBounds(body);
            if (originalBounds.size.x <= 0f || originalBounds.size.y <= 0f) return false;
            Vector2 originalRoot = root.position;
            Vector2 envelopeOffset = (Vector2)originalBounds.center - originalRoot;
            Vector2 envelopeSize = (Vector2)originalBounds.size;
            float clearance = Mathf.Max(.001f, policy.Clearance);
            float positionTolerance = Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(preferred.x), Mathf.Abs(preferred.y)) * .0000001f);
            float maxDistanceSq = (policy.MaximumDistance + positionTolerance) * (policy.MaximumDistance + positionTolerance);
            var filter = new ContactFilter2D();
            filter.SetLayerMask(options.QueryBlockingMask);
            filter.useTriggers = true;
            bool constrainDownwardLandings = policy.DownwardFloorMask.value != 0;
            var floorFilter = new ContactFilter2D { useTriggers = false };
            floorFilter.SetLayerMask(policy.DownwardFloorMask);
            floorFilter.SetNormalAngle(45f, 135f);
            bool constrainCorrections = policy.TerrainMask.value != 0;
            var terrainFilter = new ContactFilter2D { useTriggers = true };
            terrainFilter.SetLayerMask(policy.TerrainMask);
            Vector2 originalCentre = originalBounds.center;
            Vector2 requestedCentre = preferred + envelopeOffset;
            int candidateCount = 0, examined = 0, queries = 0;
            bool limited = false, seedsAdded = false;
            float planeLength = policy.HalfPlaneNormal.magnitude;
            Vector2 planeNormal = planeLength > .000001f ? policy.HalfPlaneNormal / planeLength : Vector2.zero;
            float minimumProjection = planeLength > .000001f ? policy.MinimumProjection / planeLength : 0f;
            GameObject probeObject = null;
            Rigidbody2D probeBody = null;
            CustomCollider2D probe = null;
            if (constrainCorrections)
            {
                int hits = physicsScene.OverlapPoint(requestedCentre, terrainFilter, SearchOverlaps);
                bool insideTerrain = hits == SearchOverlaps.Length;
                for (int i = 0; i < hits && !insideTerrain; i++)
                    insideTerrain = IsObstacle(SearchOverlaps[i]);
                System.Array.Clear(SearchOverlaps, 0, hits);
                if (insideTerrain || policy.RequiresLineOfSight && HasObstruction(originalCentre, requestedCentre, terrainFilter))
                    return false;
            }

            AddCandidate(preferred);
            if (planeLength > .000001f)
            {
                float deficit = minimumProjection - Vector2.Dot(planeNormal, preferred);
                if (deficit > 0f) AddCandidate(preferred + planeNormal * deficit);
            }

            try
            {
                while (examined < candidateCount && examined < policy.MaximumCandidates)
                {
                    int nearest = examined;
                    for (int i = examined + 1; i < candidateCount; i++)
                        if (LandingCandidateCosts[i] < LandingCandidateCosts[nearest] ||
                            LandingCandidateCosts[i] == LandingCandidateCosts[nearest] &&
                            (LandingCandidates[i].y > LandingCandidates[nearest].y ||
                             LandingCandidates[i].y == LandingCandidates[nearest].y &&
                             LandingCandidates[i].x < LandingCandidates[nearest].x)) nearest = i;
                    Swap(examined, nearest);
                    Vector2 candidate = LandingCandidates[examined++];
                    Vector2 centre = candidate + envelopeOffset;
                    Vector2 half = envelopeSize * .5f + Vector2.one * clearance;
                    bool clear = IsLandingEnvelopeInsideBoundary(centre, half, options.Boundary);
                    if (!clear) AddBoundaryCandidates(centre, half);

                    int count = physicsScene.OverlapBox(centre, half * 2f, 0f, filter, SearchOverlaps);
                    if (count == SearchOverlaps.Length)
                    {
                        // A truncated broad phase cannot establish a clear landing.
                        limited = true;
                        clear = false;
                        System.Array.Clear(SearchOverlaps, 0, count);
                        count = 0;
                    }

                    bool probePositioned = false;
                    for (int i = 0; i < count; i++)
                    {
                        var obstacle = SearchOverlaps[i];
                        if (!IsObstacle(obstacle) || options.IgnoreTriggers && obstacle.isTrigger &&
                            !IsLayerInMask(obstacle.gameObject.layer, options.TriggerBlockingMask)) continue;

                        // Pose-override Distance requires Rigidbody2D on BOTH inputs. Use a
                        // short-lived exact-shape probe so static walls and bodies work too.
                        if (!probe && !PrepareProbe())
                        {
                            report = new TeleportLandingSearchReport(examined, queries, limited);
                            return false;
                        }
                        if (!probePositioned) { probeBody.position = candidate; probePositioned = true; }
                        var separation = Physics2D.Distance(probe, obstacle);
                        queries++;
                        if (!separation.isValid) { clear = false; continue; }
                        if (separation.distance >= clearance - .0001f) continue;
                        clear = false;
                        // A signed distance/normal alone can be uninformative for deeply
                        // overlapping polygon cores. Generate fully separated candidates,
                        // instead of repeatedly pushing in both directions inside a wall.
                        var obstacleBounds = GetBodyBounds(obstacle);
                        AddSeparatedProjection(candidate, obstacle, obstacleBounds, separation.normal);
                        AddSeparatedProjection(candidate, obstacle, obstacleBounds, -separation.normal);
                        float padding = clearance + Physics2D.defaultContactOffset * 2f + .002f;
                        Vector2 bodyHalf = envelopeSize * .5f;
                        AddCandidate(new Vector2(obstacleBounds.min.x - bodyHalf.x - padding - envelopeOffset.x, candidate.y));
                        AddCandidate(new Vector2(obstacleBounds.max.x + bodyHalf.x + padding - envelopeOffset.x, candidate.y));
                        AddCandidate(new Vector2(candidate.x, obstacleBounds.min.y - bodyHalf.y - padding - envelopeOffset.y));
                        AddCandidate(new Vector2(candidate.x, obstacleBounds.max.y + bodyHalf.y + padding - envelopeOffset.y));
                    }
                    System.Array.Clear(SearchOverlaps, 0, count);
                    if (clear && constrainDownwardLandings && centre.y < originalCentre.y - positionTolerance)
                        clear = !HasObstruction(originalCentre, centre, floorFilter);

                    if (clear && constrainCorrections)
                    {
                        clear = !HasObstruction(requestedCentre, centre, terrainFilter) &&
                            (!policy.RequiresLineOfSight || !HasObstruction(originalCentre, centre, terrainFilter));
                    }

                    if (clear)
                        clear = TryCheckPosition(root, body, candidate, options, commit: false, out _);
                    if (!clear)
                    {
                        // Prefer geometric correction before spending the remaining budget on ring seeds.
                        if (!seedsAdded && (examined == candidateCount || examined >= Mathf.Min(16, Mathf.Max(1, policy.MaximumCandidates / 2))))
                        { AddFallbackSeeds(); seedsAdded = true; }
                        continue;
                    }

                    safePosition = candidate;
                    report = new TeleportLandingSearchReport(examined, queries, limited);
                    return true;
                }

                report = new TeleportLandingSearchReport(examined, queries,
                    limited || examined < candidateCount);
                return false;
            }
            finally
            {
                System.Array.Clear(SearchOverlaps, 0, SearchOverlaps.Length);
                LandingProbeShapes.Clear();
                if (probeObject)
                {
                    if (probe) probe.enabled = false;
                    if (probeBody) probeBody.simulated = false;
                    probeObject.SetActive(false);
                    if (Application.isPlaying) Object.Destroy(probeObject);
                    else Object.DestroyImmediate(probeObject);
                }
            }

            bool IsObstacle(Collider2D collider) =>
                collider && collider != body && collider != probe && collider.transform != root &&
                !collider.transform.IsChildOf(root) && !options.IsIgnored(collider);

            bool HasObstruction(Vector2 from, Vector2 to, ContactFilter2D pathFilter)
            {
                Vector2 displacement = to - from;
                float distance = displacement.magnitude;
                if (distance <= positionTolerance) return false;

                int count = physicsScene.Raycast(from, displacement / distance, distance, pathFilter, PathHits);
                try
                {
                    if (count == PathHits.Length) return true;
                    for (int i = 0; i < count; i++)
                        if (IsObstacle(PathHits[i].collider)) return true;
                    return false;
                }
                finally
                {
                    System.Array.Clear(PathHits, 0, count);
                }
            }

            bool PrepareProbe()
            {
                if (body.GetShapes(LandingProbeShapes) == 0) return false;
                // Physics shapes are in their attached rigid body's local coordinates
                // (world coordinates for a static collider). Preserve all baked scale,
                // rotation, offsets and radii, expressing vertices relative to the root.
                var toWorld = LandingProbeShapes.localToWorldMatrix;
                for (int shapeIndex = 0; shapeIndex < LandingProbeShapes.shapeCount; shapeIndex++)
                {
                    var shape = LandingProbeShapes.GetShape(shapeIndex);
                    for (int vertex = 0; vertex < shape.vertexCount; vertex++)
                        LandingProbeShapes.SetShapeVertex(shapeIndex, vertex,
                            (Vector2)toWorld.MultiplyPoint3x4(LandingProbeShapes.GetShapeVertex(shapeIndex, vertex)) - originalRoot);
                    if (shape.useAdjacentStart || shape.useAdjacentEnd)
                        LandingProbeShapes.SetShapeAdjacentVertices(shapeIndex, shape.useAdjacentStart, shape.useAdjacentEnd,
                            (Vector2)toWorld.MultiplyPoint3x4(shape.adjacentStart) - originalRoot,
                            (Vector2)toWorld.MultiplyPoint3x4(shape.adjacentEnd) - originalRoot);
                }
                LandingProbeShapes.localToWorldMatrix = Matrix4x4.identity;
                probeObject = new GameObject("TeleportLandingProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                probeObject.SetActive(false);
                if (physicsScene != Physics2D.defaultPhysicsScene)
                    SceneManager.MoveGameObjectToScene(probeObject, bodyScene);
                probeObject.transform.position = originalRoot;
                probeBody = probeObject.AddComponent<Rigidbody2D>();
                probeBody.bodyType = RigidbodyType2D.Kinematic;
                probeBody.gravityScale = 0f;
                probe = probeObject.AddComponent<CustomCollider2D>();
                probe.isTrigger = true;
                probe.excludeLayers = ~0;
                probe.layerOverridePriority = int.MaxValue;
                probe.callbackLayers = 0;
                probe.contactCaptureLayers = 0;
                probe.SetCustomShapes(LandingProbeShapes);
                probeObject.SetActive(true);
                Physics2D.SyncTransforms();
                var copiedBounds = GetBodyBounds(probe);
                // Fail closed if an engine/API change cannot preserve the source shape.
                return ((Vector2)copiedBounds.center - (Vector2)originalBounds.center).sqrMagnitude < .000025f &&
                       ((Vector2)copiedBounds.size - (Vector2)originalBounds.size).sqrMagnitude < .000025f;
            }

            void AddSeparatedProjection(Vector2 from, Collider2D obstacle, Bounds bounds, Vector2 direction)
            {
                if (direction.sqrMagnitude < .000001f) return;
                direction.Normalize();
                Vector2 relative = from - preferred;
                float along = Vector2.Dot(relative, direction);
                float available = -along + Mathf.Sqrt(Mathf.Max(0f, along * along + maxDistanceSq - relative.sqrMagnitude));
                float acrossPlane = Vector2.Dot(planeNormal, direction);
                if (planeLength > .000001f && acrossPlane < -.000001f)
                    available = Mathf.Min(available, Mathf.Max(0f,
                        (Vector2.Dot(planeNormal, from) - minimumProjection) / -acrossPlane));
                if (available <= positionTolerance) return;
                Vector2 combinedHalf = (Vector2)bounds.extents + envelopeSize * .5f;
                float upper = Vector2.Dot((Vector2)bounds.center - (from + envelopeOffset), direction)
                    + Mathf.Abs(direction.x) * combinedHalf.x + Mathf.Abs(direction.y) * combinedHalf.y
                    + clearance + Physics2D.defaultContactOffset * 2f + .002f;
                upper = Mathf.Min(upper, available);
                if (upper <= positionTolerance) return;
                try
                {
                    if (!Clears(upper)) return;
                    float lower = 0f;
                    // A bounded line search refines an actually clear endpoint. Concave
                    // geometry may have earlier free intervals; global optimality is not assumed.
                    for (int step = 0; step < 10 && upper - lower > positionTolerance; step++)
                    {
                        float middle = (lower + upper) * .5f;
                        if (Clears(middle)) upper = middle; else lower = middle;
                    }
                    AddCandidate(from + direction * upper);
                }
                finally { probeBody.position = from; }

                bool Clears(float distance)
                {
                    probeBody.position = from + direction * distance;
                    var separation = Physics2D.Distance(probe, obstacle);
                    queries++;
                    return separation.isValid && separation.distance >= clearance - .0001f;
                }
            }

            void AddCandidate(Vector2 position)
            {
                if (!IsFinite(position.x) || !IsFinite(position.y) ||
                    planeLength > .000001f && Vector2.Dot(planeNormal, position) < minimumProjection - .0001f) return;
                if (policy.MaximumDistance == 0f && position != preferred) return;
                float cost = (position - preferred).sqrMagnitude;
                if (cost > maxDistanceSq + .00001f) return;
                for (int i = 0; i < candidateCount; i++)
                    if ((LandingCandidates[i] - position).sqrMagnitude < .000001f) return;
                if (candidateCount == LandingCandidateCapacity) { limited = true; return; }
                LandingCandidates[candidateCount] = position;
                LandingCandidateCosts[candidateCount++] = cost;
            }

            void AddFallbackSeeds()
            {
                float firstRadius = Mathf.Max(.1f, Mathf.Min(envelopeSize.x, envelopeSize.y) * .5f);
                for (float radius = firstRadius; radius < policy.MaximumDistance; radius *= 2f) AddRing(radius);
                if (policy.MaximumDistance > 0f) AddRing(policy.MaximumDistance);
            }

            void AddRing(float radius)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * (Mathf.PI * .25f);
                    AddCandidate(preferred + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }

            void AddBoundaryCandidates(Vector2 centre, Vector2 half)
            {
                var boundary = options.Boundary;
                if (!boundary) return;
                try
                {
                    for (int path = 0; path < boundary.pathCount; path++)
                    {
                        int count = boundary.GetPath(path, BoundaryPathPoints);
                        for (int i = 0; i < count; i++)
                        {
                            Vector2 a = boundary.transform.TransformPoint(BoundaryPathPoints[i] + boundary.offset);
                            Vector2 b = boundary.transform.TransformPoint(BoundaryPathPoints[(i + 1) % count] + boundary.offset);
                            Vector2 edge = b - a;
                            if (edge.sqrMagnitude < .000001f) continue;
                            Vector2 normal = new Vector2(-edge.y, edge.x).normalized;
                            float support = Mathf.Abs(normal.x) * half.x + Mathf.Abs(normal.y) * half.y + .001f;
                            Vector2 closest = a + edge * Mathf.Clamp01(Vector2.Dot(centre - a, edge) / edge.sqrMagnitude);
                            AddCandidate(closest + normal * support - envelopeOffset);
                            AddCandidate(closest - normal * support - envelopeOffset);
                        }
                    }
                }
                finally { BoundaryPathPoints.Clear(); }
            }
        }

        private static void Swap(int a, int b)
        {
            (LandingCandidates[a], LandingCandidates[b]) = (LandingCandidates[b], LandingCandidates[a]);
            (LandingCandidateCosts[a], LandingCandidateCosts[b]) = (LandingCandidateCosts[b], LandingCandidateCosts[a]);
        }

        private static bool IsLandingEnvelopeInsideBoundary(Vector2 centre, Vector2 half, PolygonCollider2D boundary)
        {
            if (!boundary) return true;
            if (!ContainsBoundaryPoint(boundary, centre) ||
                !ContainsBoundaryPoint(boundary, centre + half) || !ContainsBoundaryPoint(boundary, centre - half) ||
                !ContainsBoundaryPoint(boundary, centre + new Vector2(half.x, -half.y)) ||
                !ContainsBoundaryPoint(boundary, centre + new Vector2(-half.x, half.y))) return false;

            // Corners alone miss concave notches and holes enclosed by the body envelope.
            Vector2 minimum = centre - half + Vector2.one * .0001f;
            Vector2 maximum = centre + half - Vector2.one * .0001f;
            try
            {
                for (int path = 0; path < boundary.pathCount; path++)
                {
                    int count = boundary.GetPath(path, BoundaryPathPoints);
                    for (int i = 0; i < count; i++)
                    {
                        Vector2 a = boundary.transform.TransformPoint(BoundaryPathPoints[i] + boundary.offset);
                        Vector2 b = boundary.transform.TransformPoint(BoundaryPathPoints[(i + 1) % count] + boundary.offset);
                        float start = 0f, end = 1f;
                        Vector2 delta = b - a;
                        if (Clip(a.x, delta.x, minimum.x, maximum.x, ref start, ref end) &&
                            Clip(a.y, delta.y, minimum.y, maximum.y, ref start, ref end)) return false;
                    }
                }
                return true;
            }
            finally { BoundaryPathPoints.Clear(); }
        }

        private static bool Clip(float origin, float delta, float minimum, float maximum, ref float start, ref float end)
        {
            if (Mathf.Abs(delta) < .000001f) return origin > minimum && origin < maximum;
            float first = (minimum - origin) / delta, last = (maximum - origin) / delta;
            if (first > last) (first, last) = (last, first);
            start = Mathf.Max(start, first);
            end = Mathf.Min(end, last);
            return start <= end;
        }

        /// <summary>Gets the complete world-space physical shape envelope, including edge radii.</summary>
        public static Bounds GetBodyBounds(Collider2D body)
        {
            if (!body || !body.isActiveAndEnabled) return default;
            try
            {
                return body.GetShapeBounds(BodyShapeBounds, useRadii: true, useWorldSpace: true);
            }
            finally
            {
                BodyShapeBounds.Clear();
            }
        }

        /// <summary>
        /// Commits an instant position only when the live body clears the requested blockers
        /// and active boundary. Failed validation restores the exact pose before simulation.
        /// </summary>
        public static bool TrySet(
            Transform root,
            Collider2D body,
            Vector2 position,
            TeleportLandingOptions2D options,
            out string failureReason) =>
            TryCheckPosition(root, body, position, options, commit: true, out failureReason);

        private static bool TryCheckPosition(Transform root, Collider2D body, Vector2 position,
            TeleportLandingOptions2D options, bool commit, out string failureReason)
        {
            failureReason = "body_unavailable";
            if (!root || !body || !body.isActiveAndEnabled ||
                body.transform != root && !body.transform.IsChildOf(root) ||
                body.attachedRigidbody && !body.attachedRigidbody.simulated)
                return false;
            failureReason = "invalid_position";
            if (!IsFinite(position.x) || !IsFinite(position.y)) return false;
            Vector3 originalPosition = root.position;
            if (!IsFinite(originalPosition.x) || !IsFinite(originalPosition.y) || !IsFinite(originalPosition.z))
                return false;
            failureReason = "missing_blocking_mask";
            if (options.BlockingMask.value == 0) return false;

            bool committed = false;
            try
            {
                var finalPosition = originalPosition;
                finalPosition.x = position.x;
                finalPosition.y = position.y;
                root.position = finalPosition;
                Physics2D.SyncTransforms();

                Bounds bounds = GetBodyBounds(body);
                failureReason = "invalid_body_bounds";
                if (bounds.size.x <= 0f || bounds.size.y <= 0f) return false;
                failureReason = "outside_active_boundary";
                if (!IsLandingEnvelopeInsideBoundary(bounds.center, bounds.extents, options.Boundary)) return false;

                var filter = new ContactFilter2D();
                filter.SetLayerMask(options.QueryBlockingMask);
                filter.useTriggers = true;
                int count = body.Overlap(filter, LandingOverlaps);
                failureReason = "landing_overlap_limit";
                if (count >= LandingOverlaps.Length) return false;
                for (int i = 0; i < count; i++)
                {
                    var hit = LandingOverlaps[i];
                    if (!hit || hit == body || hit.transform == root || hit.transform.IsChildOf(root)) continue;
                    if (options.IsIgnored(hit)) continue;
                    if (options.IgnoreTriggers && hit.isTrigger && !IsLayerInMask(hit.gameObject.layer, options.TriggerBlockingMask)) continue;
                    var separation = body.Distance(hit);
                    if (!separation.isValid || separation.isOverlapped)
                    {
                        failureReason = "blocked_by:" + hit.name;
                        return false;
                    }
                }

                committed = commit;
                failureReason = string.Empty;
                return true;
            }
            catch (System.Exception exception)
            {
                failureReason = "landing_validation_failed:" + exception.Message;
                return false;
            }
            finally
            {
                System.Array.Clear(LandingOverlaps, 0, LandingOverlaps.Length);
                if (!committed && root)
                {
                    root.position = originalPosition;
                    Physics2D.SyncTransforms();
                }
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ContainsBoundaryPoint(PolygonCollider2D boundary, Vector2 point)
        {
            if (boundary.isActiveAndEnabled) return boundary.OverlapPoint(point);

            // Disabled boundaries still contribute their authored paths.
            Vector2 localPoint = (Vector2)boundary.transform.InverseTransformPoint(point) - boundary.offset;
            bool inside = false;
            try
            {
                for (int path = 0; path < boundary.pathCount; path++)
                {
                    int count = boundary.GetPath(path, BoundaryPathPoints);
                    if (count < 3) continue;
                    for (int i = 0, j = count - 1; i < count; j = i++)
                    {
                        Vector2 a = BoundaryPathPoints[j];
                        Vector2 b = BoundaryPathPoints[i];
                        Vector2 edge = b - a;
                        Vector2 relative = localPoint - a;
                        float cross = edge.x * relative.y - edge.y * relative.x;
                        if (cross == 0f && Vector2.Dot(localPoint - a, localPoint - b) <= 0f)
                            return true;
                        if ((a.y > localPoint.y) != (b.y > localPoint.y) &&
                            localPoint.x < a.x + (localPoint.y - a.y) * edge.x / edge.y)
                            inside = !inside;
                    }
                }
                return inside;
            }
            finally
            {
                BoundaryPathPoints.Clear();
            }
        }

        private static bool IsLayerInMask(int layer, LayerMask mask)
        {
            return (mask.value & (1 << layer)) != 0;
        }
    }
}
