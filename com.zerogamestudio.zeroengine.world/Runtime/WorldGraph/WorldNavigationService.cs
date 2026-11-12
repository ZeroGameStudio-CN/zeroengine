using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    public sealed class WorldNavigationService
    {
        private readonly WorldGraphSO _graph;
        private readonly HashSet<string> _navigationReadyCellIds = new HashSet<string>();

        public WorldNavigationService(WorldGraphSO graph)
        {
            _graph = graph;
        }

        public bool IsCellNavigationReady(string cellId)
        {
            return !string.IsNullOrWhiteSpace(cellId) && _navigationReadyCellIds.Contains(cellId);
        }

        public void RegisterCellNavigationReady(string cellId)
        {
            if (!string.IsNullOrWhiteSpace(cellId))
            {
                _navigationReadyCellIds.Add(cellId);
            }
        }

        public void UnregisterCellNavigation(string cellId)
        {
            if (!string.IsNullOrWhiteSpace(cellId))
            {
                _navigationReadyCellIds.Remove(cellId);
            }
        }

        public void ReplaceReadyCells(IEnumerable<string> cellIds)
        {
            _navigationReadyCellIds.Clear();
            if (cellIds == null)
            {
                return;
            }

            foreach (var cellId in cellIds)
            {
                RegisterCellNavigationReady(cellId);
            }
        }

        public WorldNavigationRouteResult ValidateTravelLink(
            string linkId,
            string fromAnchorId = null,
            bool requireNavigationReady = true)
        {
            if (_graph == null)
            {
                return Result(WorldNavigationRouteStatus.GraphMissing, "World graph is missing.");
            }

            var link = FindTravelLink(linkId);
            if (link == null)
            {
                return Result(WorldNavigationRouteStatus.LinkNotFound, $"World travel link '{linkId}' was not found.");
            }

            if (!TryResolveTravelAnchorIds(link, fromAnchorId, out var startAnchorId, out var targetAnchorId))
            {
                return Result(
                    WorldNavigationRouteStatus.OriginMismatch,
                    $"World travel link '{link.LinkId}' cannot start from anchor '{fromAnchorId}'.",
                    linkId: link.LinkId);
            }

            if (!TryResolveAnchor(startAnchorId, out var from)
                || !TryResolveAnchor(targetAnchorId, out var to))
            {
                return Result(
                    WorldNavigationRouteStatus.AnchorNotFound,
                    $"World travel link '{link.LinkId}' references a missing anchor.",
                    linkId: link.LinkId);
            }

            return ValidateNavigationAvailability(from, to, link.LinkId, requireNavigationReady);
        }

        public WorldNavigationRouteResult ValidateAnchorRoute(
            string fromAnchorId,
            string toAnchorId,
            bool requireNavigationReady = true)
        {
            if (_graph == null)
            {
                return Result(WorldNavigationRouteStatus.GraphMissing, "World graph is missing.");
            }

            if (!TryResolveAnchor(fromAnchorId, out var from)
                || !TryResolveAnchor(toAnchorId, out var to))
            {
                return Result(WorldNavigationRouteStatus.AnchorNotFound, "World navigation route references a missing anchor.");
            }

            if (from.CellId == to.CellId)
            {
                return ValidateNavigationAvailability(from, to, linkId: null, requireNavigationReady);
            }

            var link = FindDirectTravelLink(fromAnchorId, toAnchorId);
            if (link == null)
            {
                return Result(
                    WorldNavigationRouteStatus.RouteNotConnected,
                    $"World navigation route '{fromAnchorId}' -> '{toAnchorId}' is not connected by a travel link.");
            }

            return ValidateNavigationAvailability(from, to, link.LinkId, requireNavigationReady);
        }

        private WorldNavigationRouteResult ValidateNavigationAvailability(
            ResolvedAnchor from,
            ResolvedAnchor to,
            string linkId,
            bool requireNavigationReady)
        {
            if (requireNavigationReady && !IsCellNavigationReady(from.CellId))
            {
                return Result(
                    WorldNavigationRouteStatus.NavigationUnavailable,
                    $"World cell navigation is not ready: {from.CellId}.",
                    from.Position,
                    to.Position,
                    linkId);
            }

            if (requireNavigationReady && !IsCellNavigationReady(to.CellId))
            {
                return Result(
                    WorldNavigationRouteStatus.NavigationUnavailable,
                    $"World cell navigation is not ready: {to.CellId}.",
                    from.Position,
                    to.Position,
                    linkId);
            }

            return Result(WorldNavigationRouteStatus.Succeeded, null, from.Position, to.Position, linkId);
        }

        private WorldTravelLinkDefinition FindTravelLink(string linkId)
        {
            if (string.IsNullOrWhiteSpace(linkId))
            {
                return null;
            }

            foreach (var link in _graph.TravelLinks)
            {
                if (link != null && link.LinkId == linkId)
                {
                    return link;
                }
            }

            return null;
        }

        private WorldTravelLinkDefinition FindDirectTravelLink(string fromAnchorId, string toAnchorId)
        {
            foreach (var link in _graph.TravelLinks)
            {
                if (link == null)
                {
                    continue;
                }

                if (link.FromAnchorId == fromAnchorId && link.ToAnchorId == toAnchorId)
                {
                    return link;
                }

                if (link.Bidirectional && link.FromAnchorId == toAnchorId && link.ToAnchorId == fromAnchorId)
                {
                    return link;
                }
            }

            return null;
        }

        private static bool TryResolveTravelAnchorIds(
            WorldTravelLinkDefinition link,
            string requestedFromAnchorId,
            out string startAnchorId,
            out string targetAnchorId)
        {
            if (string.IsNullOrWhiteSpace(requestedFromAnchorId)
                || requestedFromAnchorId == link.FromAnchorId)
            {
                startAnchorId = link.FromAnchorId;
                targetAnchorId = link.ToAnchorId;
                return true;
            }

            if (link.Bidirectional && requestedFromAnchorId == link.ToAnchorId)
            {
                startAnchorId = link.ToAnchorId;
                targetAnchorId = link.FromAnchorId;
                return true;
            }

            startAnchorId = null;
            targetAnchorId = null;
            return false;
        }

        private bool TryResolveAnchor(string anchorId, out ResolvedAnchor resolved)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                resolved = default;
                return false;
            }

            foreach (var region in _graph.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                foreach (var cell in region.Cells)
                {
                    if (cell == null)
                    {
                        continue;
                    }

                    foreach (var anchor in cell.Anchors)
                    {
                        if (anchor == null || anchor.AnchorId != anchorId)
                        {
                            continue;
                        }

                        var rotation = Quaternion.LookRotation(
                            NormalizedForward(anchor.CellLocalForward),
                            Vector3.up);
                        var position = new WorldPosition(
                            _graph.WorldGraphId,
                            region.RegionId,
                            cell.CellId,
                            anchor.AnchorId,
                            anchor.CellLocalPosition,
                            rotation,
                            cell.WorldOrigin + anchor.CellLocalPosition,
                            rotation);

                        resolved = new ResolvedAnchor(position);
                        return true;
                    }
                }
            }

            resolved = default;
            return false;
        }

        private static Vector3 NormalizedForward(Vector3 forward)
        {
            return forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        }

        private static WorldNavigationRouteResult Result(
            WorldNavigationRouteStatus status,
            string message,
            WorldPosition from = default,
            WorldPosition to = default,
            string linkId = null)
        {
            return new WorldNavigationRouteResult(status, from, to, linkId, message);
        }

        private readonly struct ResolvedAnchor
        {
            public ResolvedAnchor(WorldPosition position)
            {
                Position = position;
            }

            public WorldPosition Position { get; }
            public string CellId => Position.CellId;
        }
    }
}
