using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph.Traversal
{
    public enum WorldCellTraversalValidationCode
    {
        NullDefinition = 0,
        MissingCellId = 1,
        InvalidLocalBounds = 2,
        InvalidRecoveryHeight = 3,
        NullBoundary = 4,
        MissingBoundaryId = 5,
        DuplicateBoundaryId = 6,
        InvalidBoundaryBounds = 7,
        MissingBoundaryTargetCell = 8,
        NullAnchor = 9,
        MissingAnchorId = 10,
        DuplicateAnchorId = 11,
        AnchorOutsideBounds = 12,
        MissingSpawnAnchor = 13,
        MissingRecoveryAnchor = 14,
        ExitOverlapsSolid = 15
    }

    public readonly struct WorldCellTraversalValidationIssue
    {
        public WorldCellTraversalValidationIssue(
            WorldCellTraversalValidationCode code,
            string message,
            bool isBlocking = true)
        {
            Code = code;
            Message = message ?? string.Empty;
            IsBlocking = isBlocking;
        }

        public WorldCellTraversalValidationCode Code { get; }
        public string Message { get; }
        public bool IsBlocking { get; }
    }

    public static class WorldCellTraversalValidator
    {
        public static IReadOnlyList<WorldCellTraversalValidationIssue> Validate(
            WorldCellTraversalDefinition definition)
        {
            var issues = new List<WorldCellTraversalValidationIssue>();
            if (definition == null)
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.NullDefinition,
                    "Traversal definition is null."));
                return issues;
            }

            if (string.IsNullOrWhiteSpace(definition.CellId))
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.MissingCellId,
                    "Traversal definition has no stable cell id."));
            }

            if (!HasPositiveSize(definition.LocalBounds))
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.InvalidLocalBounds,
                    $"Traversal bounds for '{definition.CellId}' must have positive size."));
            }

            if (float.IsNaN(definition.RecoveryHeight)
                || float.IsInfinity(definition.RecoveryHeight))
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.InvalidRecoveryHeight,
                    $"Recovery height for '{definition.CellId}' must be finite."));
            }

            ValidateBoundaries(definition, issues);
            ValidateAnchors(definition, issues);
            ValidateExitOverlaps(definition, issues);
            return issues;
        }

        private static void ValidateBoundaries(
            WorldCellTraversalDefinition definition,
            List<WorldCellTraversalValidationIssue> issues)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var boundary in definition.Boundaries)
            {
                if (boundary == null)
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.NullBoundary,
                        $"Traversal '{definition.CellId}' contains a null boundary."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(boundary.BoundaryId))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.MissingBoundaryId,
                        $"Traversal '{definition.CellId}' contains a boundary without an id."));
                }
                else if (!ids.Add(boundary.BoundaryId))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.DuplicateBoundaryId,
                        $"Traversal '{definition.CellId}' contains duplicate boundary '{boundary.BoundaryId}'."));
                }

                if (!HasPositiveSize(boundary.LocalBounds))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.InvalidBoundaryBounds,
                        $"Boundary '{boundary.BoundaryId}' must have positive size."));
                }

                if ((boundary.Kind == WorldTraversalBoundaryKind.Portal
                     || boundary.Kind == WorldTraversalBoundaryKind.Streaming)
                    && string.IsNullOrWhiteSpace(boundary.TargetCellId))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.MissingBoundaryTargetCell,
                        $"Boundary '{boundary.BoundaryId}' requires a target cell id."));
                }
            }
        }

        private static void ValidateAnchors(
            WorldCellTraversalDefinition definition,
            List<WorldCellTraversalValidationIssue> issues)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var hasSpawn = false;
            var hasRecovery = false;
            foreach (var anchor in definition.Anchors)
            {
                if (anchor == null)
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.NullAnchor,
                        $"Traversal '{definition.CellId}' contains a null anchor."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(anchor.AnchorId))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.MissingAnchorId,
                        $"Traversal '{definition.CellId}' contains an anchor without an id."));
                }
                else if (!ids.Add(anchor.AnchorId))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.DuplicateAnchorId,
                        $"Traversal '{definition.CellId}' contains duplicate anchor '{anchor.AnchorId}'."));
                }

                if (!ContainsXZ(definition.LocalBounds, anchor.LocalPosition))
                {
                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.AnchorOutsideBounds,
                        $"Anchor '{anchor.AnchorId}' is outside traversal bounds."));
                }

                hasSpawn |= anchor.Kind == WorldTraversalAnchorKind.Spawn;
                hasRecovery |= anchor.Kind == WorldTraversalAnchorKind.Recovery;
            }

            if (!hasSpawn)
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.MissingSpawnAnchor,
                    $"Traversal '{definition.CellId}' requires a spawn anchor."));
            }

            if (!hasRecovery)
            {
                issues.Add(new WorldCellTraversalValidationIssue(
                    WorldCellTraversalValidationCode.MissingRecoveryAnchor,
                    $"Traversal '{definition.CellId}' requires a recovery anchor."));
            }
        }

        private static void ValidateExitOverlaps(
            WorldCellTraversalDefinition definition,
            List<WorldCellTraversalValidationIssue> issues)
        {
            foreach (var exit in definition.Boundaries)
            {
                if (exit == null
                    || (exit.Kind != WorldTraversalBoundaryKind.Portal
                        && exit.Kind != WorldTraversalBoundaryKind.Streaming))
                {
                    continue;
                }

                foreach (var solid in definition.Boundaries)
                {
                    if (solid == null
                        || solid.Kind != WorldTraversalBoundaryKind.Solid
                        || !exit.LocalBounds.Intersects(solid.LocalBounds))
                    {
                        continue;
                    }

                    issues.Add(new WorldCellTraversalValidationIssue(
                        WorldCellTraversalValidationCode.ExitOverlapsSolid,
                        $"Exit '{exit.BoundaryId}' overlaps solid boundary '{solid.BoundaryId}'."));
                }
            }
        }

        private static bool HasPositiveSize(Bounds bounds)
        {
            return IsFinite(bounds.center.x)
                   && IsFinite(bounds.center.y)
                   && IsFinite(bounds.center.z)
                   && IsFinite(bounds.size.x)
                   && IsFinite(bounds.size.y)
                   && IsFinite(bounds.size.z)
                   && bounds.size.x > 0f
                   && bounds.size.y > 0f
                   && bounds.size.z > 0f;
        }

        private static bool ContainsXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x
                   && point.x <= bounds.max.x
                   && point.z >= bounds.min.z
                   && point.z <= bounds.max.z;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
