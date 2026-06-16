using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ZeroEngine.World.Authoring;
using ZeroEngine.World.WorldGraph;

namespace ZeroEngine.World.Editor.WorldGraph
{
    public static class WorldGraphValidator
    {
        private const float MinimumAnchorForwardSqrMagnitude = 0.0001f;

        public static IReadOnlyList<AreaAuthoringIssue> Validate(
            WorldGraphSO graph,
            WorldGraphValidationOptions options)
        {
            var issues = new List<AreaAuthoringIssue>();
            if (graph == null)
            {
                issues.Add(Error("WORLD_GRAPH_MISSING", "WorldGraph asset is missing."));
                return issues;
            }

            if (string.IsNullOrWhiteSpace(graph.WorldGraphId))
            {
                issues.Add(Error("WORLD_GRAPH_ID_EMPTY", "WorldGraph must have a stable worldGraphId."));
            }
            else if (!IsStableId(graph.WorldGraphId))
            {
                issues.Add(Error("WORLD_GRAPH_ID_INVALID_FORMAT", $"WorldGraph id '{graph.WorldGraphId}' must use stable lowercase id characters."));
            }

            var regionIds = new HashSet<string>();
            var cellIds = new HashSet<string>();
            var anchorIds = new HashSet<string>();
            var anchorKindsById = new Dictionary<string, WorldAnchorKind>();
            var anchorCellIdsById = new Dictionary<string, string>();
            var cellKindsById = new Dictionary<string, WorldCellKind>();
            var sceneAddresses = new HashSet<string>();

            foreach (var region in graph.Regions)
            {
                if (region == null)
                {
                    issues.Add(Error("WORLD_REGION_NULL", "WorldGraph contains a null region."));
                    continue;
                }

                AddUnique(region.RegionId, regionIds, "WORLD_REGION_ID_EMPTY", "WORLD_REGION_ID_DUPLICATE", "Region id", issues);

                foreach (var cell in region.Cells)
                {
                    if (cell == null)
                    {
                        issues.Add(Error("WORLD_CELL_NULL", $"Region '{region.RegionId}' contains a null cell.", contextId: region.RegionId));
                        continue;
                    }

                    AddUnique(cell.CellId, cellIds, "WORLD_CELL_ID_EMPTY", "WORLD_CELL_ID_DUPLICATE", "Cell id", issues);
                    if (!string.IsNullOrWhiteSpace(cell.CellId) && !cellKindsById.ContainsKey(cell.CellId))
                    {
                        cellKindsById.Add(cell.CellId, cell.CellKind);
                    }

                    if (!Enum.IsDefined(typeof(WorldCellKind), cell.CellKind))
                    {
                        issues.Add(Error("WORLD_CELL_KIND_INVALID", $"Cell '{cell.CellId}' has invalid cell kind '{cell.CellKind}'.", contextId: cell.CellId));
                    }

                    if (!IsFinite(cell.WorldOrigin))
                    {
                        issues.Add(Error("WORLD_CELL_ORIGIN_INVALID", $"Cell '{cell.CellId}' has an invalid world origin {cell.WorldOrigin}.", contextId: cell.CellId));
                    }

                    if (options.RequireSceneAddresses && string.IsNullOrWhiteSpace(cell.SceneAddress))
                    {
                        issues.Add(Error("WORLD_CELL_SCENE_ADDRESS_EMPTY", $"Cell '{cell.CellId}' must have a scene address.", contextId: cell.CellId));
                    }
                    else if (options.RequireSceneAddresses && !sceneAddresses.Add(cell.SceneAddress))
                    {
                        issues.Add(Error("WORLD_CELL_SCENE_ADDRESS_DUPLICATE", $"Duplicate world cell scene address '{cell.SceneAddress}'.", contextId: cell.SceneAddress));
                    }

                    if (cell.Layers == WorldCellLayer.None)
                    {
                        issues.Add(Error("WORLD_CELL_LAYERS_EMPTY", $"Cell '{cell.CellId}' must declare at least one load layer.", contextId: cell.CellId));
                    }
                    else if ((cell.Layers & ~WorldCellLayer.All) != 0)
                    {
                        issues.Add(Error("WORLD_CELL_LAYERS_INVALID", $"Cell '{cell.CellId}' has unsupported load layer bits '{cell.Layers}'.", contextId: cell.CellId));
                    }

                    if (cell.BudgetWeight <= 0 || cell.BudgetWeight > options.MaxCellBudgetWeight)
                    {
                        issues.Add(Error("WORLD_CELL_BUDGET_WEIGHT_INVALID", $"Cell '{cell.CellId}' has invalid budget weight {cell.BudgetWeight}.", contextId: cell.CellId));
                    }

                    foreach (var anchor in cell.Anchors)
                    {
                        if (anchor == null)
                        {
                            issues.Add(Error("WORLD_ANCHOR_NULL", $"Cell '{cell.CellId}' contains a null anchor.", contextId: cell.CellId));
                            continue;
                        }

                        AddUnique(anchor.AnchorId, anchorIds, "WORLD_ANCHOR_ID_EMPTY", "WORLD_ANCHOR_ID_DUPLICATE", "Anchor id", issues);
                        if (!Enum.IsDefined(typeof(WorldAnchorKind), anchor.AnchorKind))
                        {
                            issues.Add(Error("WORLD_ANCHOR_KIND_INVALID", $"Anchor '{anchor.AnchorId}' has invalid anchor kind '{anchor.AnchorKind}'.", contextId: anchor.AnchorId));
                        }

                        if (!IsFinite(anchor.CellLocalPosition))
                        {
                            issues.Add(Error("WORLD_ANCHOR_POSITION_INVALID", $"Anchor '{anchor.AnchorId}' has an invalid local position {anchor.CellLocalPosition}.", contextId: anchor.AnchorId));
                        }

                        if (!IsFinite(anchor.CellLocalForward)
                            || anchor.CellLocalForward.sqrMagnitude < MinimumAnchorForwardSqrMagnitude)
                        {
                            issues.Add(Error("WORLD_ANCHOR_FORWARD_INVALID", $"Anchor '{anchor.AnchorId}' must have a finite non-zero local forward vector.", contextId: anchor.AnchorId));
                        }

                        if (!string.IsNullOrWhiteSpace(anchor.AnchorId))
                        {
                            anchorKindsById[anchor.AnchorId] = anchor.AnchorKind;
                            if (!anchorCellIdsById.ContainsKey(anchor.AnchorId))
                            {
                                anchorCellIdsById.Add(anchor.AnchorId, cell.CellId);
                            }
                        }
                    }
                }
            }

            ValidateStreamingBoundaries(graph, cellIds, issues);
            ValidateTravelLinks(graph, anchorIds, anchorCellIdsById, anchorKindsById, issues);

            if (options.RequireInteriorReturnLinks)
            {
                ValidateInteriorReturnLinks(graph, anchorCellIdsById, cellKindsById, issues);
            }

            ValidateFastTravelNodes(graph, anchorIds, anchorKindsById, issues);

            return issues;
        }

        private static void ValidateStreamingBoundaries(
            WorldGraphSO graph,
            HashSet<string> cellIds,
            List<AreaAuthoringIssue> issues)
        {
            var boundaryIds = new HashSet<string>();
            foreach (var cell in graph.Regions
                         .Where(region => region != null)
                         .SelectMany(region => region.Cells)
                         .Where(cell => cell != null))
            {
                var sourceCellTargetBoundaryIds = new Dictionary<string, string>();
                foreach (var boundary in cell.StreamingBoundaries)
                {
                    if (boundary == null)
                    {
                        issues.Add(Error("WORLD_STREAMING_BOUNDARY_NULL", $"Cell '{cell.CellId}' contains a null streaming boundary.", contextId: cell.CellId));
                        continue;
                    }

                    AddUnique(
                        boundary.BoundaryId,
                        boundaryIds,
                        "WORLD_STREAMING_BOUNDARY_ID_EMPTY",
                        "WORLD_STREAMING_BOUNDARY_ID_DUPLICATE",
                        "Streaming boundary id",
                        issues);

                    var targetCellIds = boundary.TargetCellIds;
                    if (targetCellIds == null || targetCellIds.Count == 0)
                    {
                        issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGETS_EMPTY", $"Streaming boundary '{boundary.BoundaryId}' must reference at least one target cell.", contextId: cell.CellId));
                        continue;
                    }

                    var knownTargets = new HashSet<string>();
                    foreach (var targetCellId in targetCellIds)
                    {
                        if (string.IsNullOrWhiteSpace(targetCellId))
                        {
                            issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGET_EMPTY", $"Streaming boundary '{boundary.BoundaryId}' contains an empty target cell id.", contextId: cell.CellId));
                            continue;
                        }

                        if (!knownTargets.Add(targetCellId))
                        {
                            issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGET_DUPLICATE", $"Streaming boundary '{boundary.BoundaryId}' references target cell '{targetCellId}' more than once.", contextId: cell.CellId));
                        }
                        else if (sourceCellTargetBoundaryIds.TryGetValue(targetCellId, out var existingBoundaryId))
                        {
                            issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGET_CELL_DUPLICATE", $"Cell '{cell.CellId}' has multiple streaming boundaries targeting cell '{targetCellId}' ('{existingBoundaryId}' and '{boundary.BoundaryId}').", contextId: cell.CellId));
                        }
                        else
                        {
                            sourceCellTargetBoundaryIds.Add(targetCellId, boundary.BoundaryId);
                        }

                        if (targetCellId == cell.CellId)
                        {
                            issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGET_SELF", $"Streaming boundary '{boundary.BoundaryId}' cannot target its own source cell '{cell.CellId}'.", contextId: cell.CellId));
                        }

                        if (!cellIds.Contains(targetCellId))
                        {
                            issues.Add(Error("WORLD_STREAMING_BOUNDARY_TARGET_MISSING", $"Streaming boundary '{boundary.BoundaryId}' references missing cell '{targetCellId}'.", contextId: cell.CellId));
                        }
                    }
                }
            }
        }

        private static void ValidateTravelLinks(
            WorldGraphSO graph,
            HashSet<string> anchorIds,
            IReadOnlyDictionary<string, string> anchorCellIdsById,
            IReadOnlyDictionary<string, WorldAnchorKind> anchorKindsById,
            List<AreaAuthoringIssue> issues)
        {
            var linkIds = new HashSet<string>();
            foreach (var link in graph.TravelLinks)
            {
                if (link == null)
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_NULL", "WorldGraph contains a null travel link."));
                    continue;
                }

                AddUnique(link.LinkId, linkIds, "WORLD_TRAVEL_LINK_ID_EMPTY", "WORLD_TRAVEL_LINK_ID_DUPLICATE", "Travel link id", issues);

                if (!Enum.IsDefined(typeof(WorldTravelMode), link.TravelMode))
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_MODE_INVALID", $"Travel link '{link.LinkId}' has invalid travel mode '{link.TravelMode}'.", contextId: link.LinkId));
                }

                if (!string.IsNullOrWhiteSpace(link.FromAnchorId)
                    && link.FromAnchorId == link.ToAnchorId)
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_SELF_ANCHOR", $"Travel link '{link.LinkId}' cannot start and end at the same anchor '{link.FromAnchorId}'.", contextId: link.LinkId));
                }

                if (anchorCellIdsById.TryGetValue(link.FromAnchorId, out var fromCellId)
                    && anchorCellIdsById.TryGetValue(link.ToAnchorId, out var toCellId)
                    && fromCellId == toCellId)
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_SAME_CELL", $"Travel link '{link.LinkId}' cannot connect anchors inside the same cell '{fromCellId}'.", contextId: link.LinkId));
                }

                ValidateTravelLinkAnchorKind(link, link.FromAnchorId, anchorKindsById, issues);
                ValidateTravelLinkAnchorKind(link, link.ToAnchorId, anchorKindsById, issues);

                if (!anchorIds.Contains(link.FromAnchorId))
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_ANCHOR_MISSING", $"Travel link '{link.LinkId}' references missing from anchor '{link.FromAnchorId}'.", contextId: link.LinkId));
                }

                if (!anchorIds.Contains(link.ToAnchorId))
                {
                    issues.Add(Error("WORLD_TRAVEL_LINK_ANCHOR_MISSING", $"Travel link '{link.LinkId}' references missing to anchor '{link.ToAnchorId}'.", contextId: link.LinkId));
                }
            }
        }

        private static void ValidateTravelLinkAnchorKind(
            WorldTravelLinkDefinition link,
            string anchorId,
            IReadOnlyDictionary<string, WorldAnchorKind> anchorKindsById,
            List<AreaAuthoringIssue> issues)
        {
            if (!anchorKindsById.TryGetValue(anchorId, out var anchorKind))
            {
                return;
            }

            if (IsAnchorKindAllowedForTravelMode(link.TravelMode, anchorKind))
            {
                return;
            }

            issues.Add(Error("WORLD_TRAVEL_LINK_ANCHOR_KIND_MISMATCH", $"Travel link '{link.LinkId}' uses {link.TravelMode} but anchor '{anchorId}' is {anchorKind}.", contextId: link.LinkId));
        }

        private static bool IsAnchorKindAllowedForTravelMode(
            WorldTravelMode travelMode,
            WorldAnchorKind anchorKind)
        {
            switch (travelMode)
            {
                case WorldTravelMode.SeamlessWalk:
                    return anchorKind == WorldAnchorKind.RoadExit;
                case WorldTravelMode.SeamlessInterior:
                    return anchorKind == WorldAnchorKind.InteriorEntry
                           || anchorKind == WorldAnchorKind.InteriorExit;
                case WorldTravelMode.PortalTransition:
                    return anchorKind == WorldAnchorKind.Portal
                           || anchorKind == WorldAnchorKind.RoadExit;
                case WorldTravelMode.FastTravel:
                    return anchorKind == WorldAnchorKind.FastTravel
                           || anchorKind == WorldAnchorKind.BattleReturn;
                default:
                    return true;
            }
        }

        private static void ValidateInteriorReturnLinks(
            WorldGraphSO graph,
            IReadOnlyDictionary<string, string> anchorCellIdsById,
            IReadOnlyDictionary<string, WorldCellKind> cellKindsById,
            List<AreaAuthoringIssue> issues)
        {
            var travelLinks = graph.TravelLinks.Where(link => link != null).ToArray();

            foreach (var interiorCell in graph.Regions
                         .Where(region => region != null)
                         .SelectMany(region => region.Cells)
                         .Where(cell => cell != null && cell.CellKind == WorldCellKind.Interior))
            {
                var hasReturn = interiorCell.Anchors.Any(anchor =>
                    anchor != null
                    && anchor.AnchorKind == WorldAnchorKind.InteriorExit
                    && InteriorExitLinksToNonInteriorCell(anchor.AnchorId, travelLinks, anchorCellIdsById, cellKindsById));
                if (!hasReturn)
                {
                    issues.Add(Error("WORLD_INTERIOR_RETURN_LINK_MISSING", $"Interior cell '{interiorCell.CellId}' must have an InteriorExit anchor linked back to an exterior anchor.", contextId: interiorCell.CellId));
                }
            }
        }

        private static bool InteriorExitLinksToNonInteriorCell(
            string anchorId,
            IReadOnlyList<WorldTravelLinkDefinition> travelLinks,
            IReadOnlyDictionary<string, string> anchorCellIdsById,
            IReadOnlyDictionary<string, WorldCellKind> cellKindsById)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                return false;
            }

            foreach (var link in travelLinks)
            {
                if (link.TravelMode != WorldTravelMode.SeamlessInterior)
                {
                    continue;
                }

                var otherAnchorId = string.Empty;
                if (link.FromAnchorId == anchorId)
                {
                    otherAnchorId = link.ToAnchorId;
                }
                else if (link.ToAnchorId == anchorId)
                {
                    otherAnchorId = link.FromAnchorId;
                }

                if (string.IsNullOrWhiteSpace(otherAnchorId))
                {
                    continue;
                }

                if (anchorCellIdsById.TryGetValue(otherAnchorId, out var otherCellId)
                    && cellKindsById.TryGetValue(otherCellId, out var otherCellKind)
                    && otherCellKind != WorldCellKind.Interior)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateFastTravelNodes(
            WorldGraphSO graph,
            HashSet<string> anchorIds,
            IReadOnlyDictionary<string, WorldAnchorKind> anchorKindsById,
            List<AreaAuthoringIssue> issues)
        {
            var nodeIds = new HashSet<string>();
            var nodeAnchorIds = new HashSet<string>();
            foreach (var node in graph.FastTravelNodes)
            {
                if (node == null)
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_NODE_NULL", "WorldGraph contains a null fast travel node."));
                    continue;
                }

                AddUnique(node.NodeId, nodeIds, "WORLD_FAST_TRAVEL_NODE_ID_EMPTY", "WORLD_FAST_TRAVEL_NODE_ID_DUPLICATE", "Fast travel node id", issues);

                if (!string.IsNullOrWhiteSpace(node.AnchorId) && !nodeAnchorIds.Add(node.AnchorId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_NODE_ANCHOR_DUPLICATE", $"Fast travel anchor '{node.AnchorId}' is registered by more than one node.", contextId: node.AnchorId));
                }

                if (!anchorIds.Contains(node.AnchorId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_ANCHOR_MISSING", $"Fast travel node '{node.NodeId}' references missing anchor '{node.AnchorId}'.", contextId: node.NodeId));
                }
                else if (anchorKindsById.TryGetValue(node.AnchorId, out var anchorKind)
                         && anchorKind != WorldAnchorKind.FastTravel)
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_NODE_ANCHOR_KIND_INVALID", $"Fast travel node '{node.NodeId}' must reference a FastTravel anchor, but '{node.AnchorId}' is {anchorKind}.", contextId: node.NodeId));
                }

                if (string.IsNullOrWhiteSpace(node.UnlockFactId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_UNLOCK_EMPTY", $"Fast travel node '{node.NodeId}' must have an unlock fact id.", contextId: node.NodeId));
                }
                else if (!IsStableId(node.UnlockFactId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_UNLOCK_INVALID_FORMAT", $"Fast travel node '{node.NodeId}' has invalid unlock fact id '{node.UnlockFactId}'.", contextId: node.NodeId));
                }
            }

            foreach (var link in graph.TravelLinks.Where(link => link != null && link.TravelMode == WorldTravelMode.FastTravel))
            {
                if (!nodeAnchorIds.Contains(link.ToAnchorId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_LINK_NODE_MISSING", $"Fast travel link '{link.LinkId}' must target a registered fast travel node anchor '{link.ToAnchorId}'.", contextId: link.LinkId));
                }

                if (link.Bidirectional && !nodeAnchorIds.Contains(link.FromAnchorId))
                {
                    issues.Add(Error("WORLD_FAST_TRAVEL_LINK_NODE_MISSING", $"Bidirectional fast travel link '{link.LinkId}' must start from a registered fast travel node anchor '{link.FromAnchorId}'.", contextId: link.LinkId));
                }
            }
        }

        private static void AddUnique(
            string id,
            HashSet<string> knownIds,
            string emptyCode,
            string duplicateCode,
            string label,
            List<AreaAuthoringIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                issues.Add(Error(emptyCode, $"{label} must not be empty."));
                return;
            }

            if (!IsStableId(id))
            {
                issues.Add(Error(InvalidFormatCode(emptyCode), $"{label} '{id}' must use stable lowercase id characters.", contextId: id));
                return;
            }

            if (!knownIds.Add(id))
            {
                issues.Add(Error(duplicateCode, $"Duplicate {label.ToLowerInvariant()} '{id}'.", contextId: id));
            }
        }

        private static string InvalidFormatCode(string emptyCode)
        {
            const string emptySuffix = "_EMPTY";
            if (emptyCode.EndsWith(emptySuffix, StringComparison.Ordinal))
            {
                return emptyCode.Substring(0, emptyCode.Length - emptySuffix.Length) + "_INVALID_FORMAT";
            }

            return emptyCode + "_INVALID_FORMAT";
        }

        private static bool IsStableId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
            {
                return false;
            }

            if (!IsStableIdAlphaNumeric(id[0]) || !IsStableIdAlphaNumeric(id[id.Length - 1]))
            {
                return false;
            }

            foreach (var character in id)
            {
                if (IsStableIdAlphaNumeric(character)
                    || character == '.'
                    || character == '_'
                    || character == '-')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsStableIdAlphaNumeric(char character)
        {
            return character >= 'a' && character <= 'z'
                   || character >= '0' && character <= '9';
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x)
                   && IsFinite(value.y)
                   && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static AreaAuthoringIssue Error(string code, string message, string assetPath = null, string contextId = null)
        {
            return new AreaAuthoringIssue(AreaAuthoringIssueSeverity.Error, code, message, assetPath, contextId);
        }
    }
}
