using System;
using System.Collections.Generic;
using System.Linq;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldGraphGraduationRunner
    {
        public static IReadOnlyList<AreaAuthoringIssue> Validate(WorldGraphGraduationProfile profile)
        {
            if (profile == null)
            {
                return new[] { Error("WORLD_GRADUATION_PROFILE_MISSING", "WorldGraph graduation profile is missing.") };
            }

            var issues = new List<AreaAuthoringIssue>();
            issues.AddRange(WorldGraphValidator.Validate(profile.Graph, WorldGraphValidationOptions.StrictProduction));
            if (profile.ConnectionNetwork != null)
            {
                issues.AddRange(WorldGraphConnectionValidator.Validate(
                    profile.ConnectionNetwork,
                    profile.ConnectedGraphsById));
            }

            issues.AddRange(ValidateRuntimeBindings(profile));
            issues.AddRange(ValidateTravelCoverage(profile));
            issues.AddRange(WorldAddressablesBindingValidator.Validate(profile));
            issues.AddRange(WorldSceneContractValidator.Validate(profile));
            issues.AddRange(WorldNavigationBindingValidator.Validate(profile));

            return issues
                .OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.AssetPath, StringComparer.Ordinal)
                .ThenBy(issue => issue.ContextId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray();
        }

        private static IEnumerable<AreaAuthoringIssue> ValidateRuntimeBindings(WorldGraphGraduationProfile profile)
        {
            var graph = profile.Graph;
            if (graph == null)
            {
                yield return Error("WORLD_GRADUATION_GRAPH_MISSING", "WorldGraph asset is missing.", profile.GraphAssetPath);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(profile.ExpectedWorldGraphId)
                && !string.Equals(graph.WorldGraphId, profile.ExpectedWorldGraphId, StringComparison.Ordinal))
            {
                yield return Error(
                    "WORLD_GRADUATION_GRAPH_ID_MISMATCH",
                    $"WorldGraph id must be {profile.ExpectedWorldGraphId}, but found {graph.WorldGraphId}.",
                    profile.GraphAssetPath,
                    graph.WorldGraphId);
            }

            var startCell = graph.FindCell(profile.StartCellId);
            if (!string.IsNullOrWhiteSpace(profile.StartCellId) && startCell == null)
            {
                yield return Error(
                    "WORLD_GRADUATION_START_CELL_MISSING",
                    $"Start cell is missing: {profile.StartCellId}.",
                    profile.GraphAssetPath,
                    profile.StartCellId);
                yield break;
            }

            var startAnchor = graph.FindAnchor(profile.StartAnchorId);
            if (!string.IsNullOrWhiteSpace(profile.StartAnchorId) && startAnchor == null)
            {
                yield return Error(
                    "WORLD_GRADUATION_START_ANCHOR_MISSING",
                    $"Start anchor is missing: {profile.StartAnchorId}.",
                    profile.GraphAssetPath,
                    profile.StartAnchorId);
                yield break;
            }

            if (startCell != null && startAnchor != null && !CellContainsAnchor(startCell, startAnchor.AnchorId))
            {
                yield return Error(
                    "WORLD_GRADUATION_START_ANCHOR_WRONG_CELL",
                    $"Start anchor {profile.StartAnchorId} must belong to start cell {profile.StartCellId}.",
                    profile.GraphAssetPath,
                    profile.StartAnchorId);
            }
        }

        private static IEnumerable<AreaAuthoringIssue> ValidateTravelCoverage(WorldGraphGraduationProfile profile)
        {
            var graph = profile.Graph;
            if (graph == null)
            {
                yield break;
            }

            var links = graph.TravelLinks.Where(link => link != null).ToArray();
            foreach (var mode in profile.RequiredTravelModes)
            {
                if (links.Any(link => link.TravelMode == mode))
                {
                    continue;
                }

                yield return Error(
                    "WORLD_GRADUATION_REQUIRED_TRAVEL_MODE_MISSING",
                    $"WorldGraph must include {mode} travel.",
                    profile.GraphAssetPath,
                    mode.ToString());
            }

            foreach (var link in links)
            {
                foreach (var issue in ValidateTravelRoute(graph, profile, link, link.FromAnchorId))
                {
                    yield return issue;
                }

                if (link.Bidirectional)
                {
                    foreach (var issue in ValidateTravelRoute(graph, profile, link, link.ToAnchorId))
                    {
                        yield return issue;
                    }
                }
            }

            if (links.Any(link => link.TravelMode == WorldTravelMode.SeamlessInterior)
                && !links.Any(link => link.TravelMode == WorldTravelMode.SeamlessInterior
                                      && TryFindCellContainingAnchor(graph, link.FromAnchorId, out var fromCell)
                                      && TryFindCellContainingAnchor(graph, link.ToAnchorId, out var toCell)
                                      && (fromCell.CellKind == WorldCellKind.Interior || toCell.CellKind == WorldCellKind.Interior)))
            {
                yield return Error(
                    "WORLD_GRADUATION_INTERIOR_LINK_NO_INTERIOR_CELL",
                    "At least one SeamlessInterior link must connect to an interior cell.",
                    profile.GraphAssetPath);
            }
        }

        private static IEnumerable<AreaAuthoringIssue> ValidateTravelRoute(
            WorldGraphSO graph,
            WorldGraphGraduationProfile profile,
            WorldTravelLinkDefinition link,
            string fromAnchorId)
        {
            var navigation = new WorldNavigationService(graph);
            var route = navigation.ValidateTravelLink(link.LinkId, fromAnchorId, requireNavigationReady: false);
            if (!route.Succeeded)
            {
                yield return Error(
                    "WORLD_GRADUATION_TRAVEL_ROUTE_INVALID",
                    $"Travel link {link.LinkId} must route from {fromAnchorId}: {route.Status} {route.Message}",
                    profile.GraphAssetPath,
                    link.LinkId);
            }

            if (link.TravelMode != WorldTravelMode.SeamlessWalk)
            {
                yield break;
            }

            if (!TryFindCellContainingAnchor(graph, fromAnchorId, out var sourceCell))
            {
                yield return Error(
                    "WORLD_GRADUATION_SEAMLESS_WALK_SOURCE_CELL_MISSING",
                    $"Seamless walk source anchor is not owned by any cell: {fromAnchorId}.",
                    profile.GraphAssetPath,
                    fromAnchorId);
                yield break;
            }

            var targetAnchorId = string.Equals(fromAnchorId, link.FromAnchorId, StringComparison.Ordinal)
                ? link.ToAnchorId
                : link.FromAnchorId;
            if (!TryFindCellContainingAnchor(graph, targetAnchorId, out var targetCell))
            {
                yield return Error(
                    "WORLD_GRADUATION_SEAMLESS_WALK_TARGET_CELL_MISSING",
                    $"Seamless walk target anchor is not owned by any cell: {targetAnchorId}.",
                    profile.GraphAssetPath,
                    targetAnchorId);
                yield break;
            }

            if (sourceCell.StreamingBoundaries.Any(boundary =>
                    boundary != null && boundary.TargetCellIds.Contains(targetCell.CellId)))
            {
                yield break;
            }

            yield return Error(
                "WORLD_GRADUATION_SEAMLESS_WALK_BOUNDARY_MISSING",
                $"Seamless walk link {link.LinkId} must stream target cell {targetCell.CellId} from source cell {sourceCell.CellId}.",
                profile.GraphAssetPath,
                link.LinkId);
        }

        internal static IEnumerable<WorldCellDefinition> EnumerateCells(WorldGraphSO graph)
        {
            if (graph == null)
            {
                return Array.Empty<WorldCellDefinition>();
            }

            return graph.Regions
                .Where(region => region != null)
                .SelectMany(region => region.Cells)
                .Where(cell => cell != null);
        }

        internal static bool TryFindCellContainingAnchor(
            WorldGraphSO graph,
            string anchorId,
            out WorldCellDefinition cell)
        {
            foreach (var candidate in EnumerateCells(graph))
            {
                if (CellContainsAnchor(candidate, anchorId))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = null;
            return false;
        }

        private static bool CellContainsAnchor(WorldCellDefinition cell, string anchorId)
        {
            return cell != null
                   && !string.IsNullOrWhiteSpace(anchorId)
                   && cell.Anchors.Any(anchor => anchor != null && anchor.AnchorId == anchorId);
        }

        internal static AreaAuthoringIssue Error(
            string code,
            string message,
            string assetPath = null,
            string contextId = null)
        {
            return new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, assetPath, contextId);
        }
    }
}
