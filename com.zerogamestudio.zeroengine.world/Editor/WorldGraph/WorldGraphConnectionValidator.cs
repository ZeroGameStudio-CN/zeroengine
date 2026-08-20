using System;
using System.Collections.Generic;
using System.Linq;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldGraphConnectionValidator
    {
        public static IReadOnlyList<AreaAuthoringIssue> Validate(
            WorldGraphConnectionNetworkSO network,
            IReadOnlyDictionary<string, WorldGraphSO> graphsById)
        {
            if (network == null)
            {
                return Array.Empty<AreaAuthoringIssue>();
            }

            var graphLookup = graphsById ?? new Dictionary<string, WorldGraphSO>();
            var issues = new List<AreaAuthoringIssue>();
            foreach (var connection in network.Connections.Where(connection => connection != null))
            {
                ValidateConnection(connection, graphLookup, issues);
            }

            return issues
                .OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.ContextId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray();
        }

        private static void ValidateConnection(
            WorldGraphConnectionDefinition connection,
            IReadOnlyDictionary<string, WorldGraphSO> graphsById,
            ICollection<AreaAuthoringIssue> issues)
        {
            if (!connection.IsWalkable)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(connection.TargetWorldGraphAddress))
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_TARGET_ADDRESS_MISSING",
                    $"Connection {connection.ConnectionId} from {connection.SourceWorldGraphId} to {connection.TargetWorldGraphId} is missing target graph address.",
                    connection.ConnectionId);
            }

            if (!graphsById.TryGetValue(connection.SourceWorldGraphId, out var sourceGraph) || sourceGraph == null)
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_SOURCE_GRAPH_MISSING",
                    $"Connection {connection.ConnectionId} source graph is missing: {connection.SourceWorldGraphId}.",
                    connection.SourceWorldGraphId);
                return;
            }

            if (!graphsById.TryGetValue(connection.TargetWorldGraphId, out var targetGraph) || targetGraph == null)
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_TARGET_GRAPH_MISSING",
                    $"Connection {connection.ConnectionId} from {connection.SourceWorldGraphId} targets missing graph {connection.TargetWorldGraphId}.",
                    connection.TargetWorldGraphId);
                return;
            }

            ValidateSource(connection, sourceGraph, issues);
            ValidateTarget(connection, targetGraph, issues);
        }

        private static void ValidateSource(
            WorldGraphConnectionDefinition connection,
            WorldGraphSO sourceGraph,
            ICollection<AreaAuthoringIssue> issues)
        {
            var sourceCell = sourceGraph.FindCell(connection.SourceCellId);
            if (sourceCell == null)
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_SOURCE_CELL_MISSING",
                    $"Connection {connection.ConnectionId} source cell is missing: {connection.SourceCellId}.",
                    connection.SourceCellId);
                return;
            }

            if (!sourceCell.StreamingBoundaries.Any(boundary =>
                    boundary != null && boundary.BoundaryId == connection.SourceBoundaryId))
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_SOURCE_BOUNDARY_MISSING",
                    $"Connection {connection.ConnectionId} source boundary is missing: {connection.SourceBoundaryId}.",
                    connection.SourceBoundaryId);
            }

            if (!CellContainsAnchor(sourceCell, connection.SourceAnchorId))
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_SOURCE_ANCHOR_MISSING",
                    $"Connection {connection.ConnectionId} source anchor is missing from source cell: {connection.SourceAnchorId}.",
                    connection.SourceAnchorId);
            }
        }

        private static void ValidateTarget(
            WorldGraphConnectionDefinition connection,
            WorldGraphSO targetGraph,
            ICollection<AreaAuthoringIssue> issues)
        {
            var targetCell = targetGraph.FindCell(connection.TargetCellId);
            if (targetCell == null)
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_TARGET_CELL_MISSING",
                    $"Connection {connection.ConnectionId} target cell is missing: {connection.TargetCellId}.",
                    connection.TargetCellId);
                return;
            }

            if (string.IsNullOrWhiteSpace(targetCell.SceneAddress))
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_TARGET_SCENE_ADDRESS_MISSING",
                    $"Connection {connection.ConnectionId} target cell has no additive scene address: {connection.TargetCellId}.",
                    connection.TargetCellId);
            }

            if (!CellContainsAnchor(targetCell, connection.TargetAnchorId))
            {
                AddError(
                    issues,
                    "WORLD_GRAPH_CONNECTION_TARGET_ANCHOR_MISSING",
                    $"Connection {connection.ConnectionId} target anchor is missing from target cell: {connection.TargetAnchorId}.",
                    connection.TargetAnchorId);
            }
        }

        private static bool CellContainsAnchor(WorldCellDefinition cell, string anchorId)
        {
            return cell != null
                   && !string.IsNullOrWhiteSpace(anchorId)
                   && cell.Anchors.Any(anchor => anchor != null && anchor.AnchorId == anchorId);
        }

        private static void AddError(
            ICollection<AreaAuthoringIssue> issues,
            string code,
            string message,
            string contextId)
        {
            issues.Add(new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, null, contextId));
        }
    }
}
