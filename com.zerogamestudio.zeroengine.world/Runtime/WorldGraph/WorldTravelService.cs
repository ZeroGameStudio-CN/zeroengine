using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    public sealed class WorldTravelService
    {
        private readonly WorldGraphSO _graph;
        private readonly WorldStreamingService _streaming;
        private bool _travelInProgress;

        public WorldTravelService(WorldGraphSO graph, WorldStreamingService streaming)
        {
            _graph = graph;
            _streaming = streaming;
        }

        public async Task<WorldTravelResult> TravelAsync(
            WorldTravelRequest request,
            CancellationToken cancellationToken)
        {
            if (_travelInProgress)
            {
                return Result(WorldTravelResultStatus.Busy, default, "A world travel operation is already in progress.");
            }

            _travelInProgress = true;
            try
            {
                if (_graph == null || _streaming == null)
                {
                    return Result(WorldTravelResultStatus.GraphMissing, default, "World graph or streaming service is missing.");
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Result(WorldTravelResultStatus.Cancelled, default, "World travel was cancelled.");
                }

                if (string.IsNullOrWhiteSpace(request.LinkId))
                {
                    return Result(WorldTravelResultStatus.LinkNotFound, default, "World travel link id is missing.");
                }

                var link = _graph.TravelLinks.FirstOrDefault(candidate =>
                    candidate != null && candidate.LinkId == request.LinkId);
                if (link == null)
                {
                    return Result(WorldTravelResultStatus.LinkNotFound, default, $"World travel link '{request.LinkId}' was not found.");
                }

                if (!TryResolveTravelAnchors(link, request.FromAnchorId, out var originAnchorId, out var destinationAnchorId))
                {
                    return Result(
                        WorldTravelResultStatus.OriginMismatch,
                        default,
                        $"World travel link '{request.LinkId}' cannot start from anchor '{request.FromAnchorId}'.");
                }

                var origin = ResolveAnchor(originAnchorId);
                if (!origin.HasAnchor)
                {
                    return Result(WorldTravelResultStatus.AnchorNotFound, default, $"World travel anchor '{originAnchorId}' was not found.");
                }

                if (string.IsNullOrWhiteSpace(_streaming.ActiveCellId))
                {
                    return Result(
                        WorldTravelResultStatus.OriginMismatch,
                        default,
                        $"World travel link '{request.LinkId}' requires an active source cell.");
                }

                if (!string.Equals(_streaming.ActiveCellId, origin.Position.CellId, StringComparison.Ordinal))
                {
                    return Result(
                        WorldTravelResultStatus.OriginMismatch,
                        default,
                        $"World travel link '{request.LinkId}' starts from cell '{origin.Position.CellId}' but active cell is '{_streaming.ActiveCellId}'.");
                }

                var destination = ResolveAnchor(destinationAnchorId);
                if (!destination.HasAnchor)
                {
                    return Result(WorldTravelResultStatus.AnchorNotFound, default, $"World travel anchor '{destinationAnchorId}' was not found.");
                }

                var streamingResult = await _streaming.ActivateCellAsync(
                    destination.Position.CellId,
                    destination.CellLayers,
                    loadBoundaryCells: true,
                    cancellationToken);

                if (streamingResult.Status == WorldStreamingResultStatus.Busy)
                {
                    return Result(WorldTravelResultStatus.Busy, default, streamingResult.Message);
                }

                if (streamingResult.Status == WorldStreamingResultStatus.Cancelled)
                {
                    return Result(WorldTravelResultStatus.Cancelled, default, streamingResult.Message);
                }

                if (!streamingResult.Succeeded)
                {
                    return Result(WorldTravelResultStatus.StreamingFailed, default, streamingResult.Message);
                }

                return Result(WorldTravelResultStatus.Succeeded, destination.Position);
            }
            catch (OperationCanceledException)
            {
                return Result(WorldTravelResultStatus.Cancelled, default, "World travel was cancelled.");
            }
            finally
            {
                _travelInProgress = false;
            }
        }

        private static bool TryResolveTravelAnchors(
            WorldTravelLinkDefinition link,
            string requestedFromAnchorId,
            out string originAnchorId,
            out string destinationAnchorId)
        {
            if (string.IsNullOrWhiteSpace(requestedFromAnchorId)
                || requestedFromAnchorId == link.FromAnchorId)
            {
                originAnchorId = link.FromAnchorId;
                destinationAnchorId = link.ToAnchorId;
                return true;
            }

            if (link.Bidirectional && requestedFromAnchorId == link.ToAnchorId)
            {
                originAnchorId = link.ToAnchorId;
                destinationAnchorId = link.FromAnchorId;
                return true;
            }

            originAnchorId = null;
            destinationAnchorId = null;
            return false;
        }

        private ResolvedAnchor ResolveAnchor(string anchorId)
        {
            foreach (var region in _graph.Regions.Where(region => region != null))
            {
                foreach (var cell in region.Cells.Where(cell => cell != null))
                {
                    var anchor = cell.Anchors.FirstOrDefault(candidate => candidate != null && candidate.AnchorId == anchorId);
                    if (anchor == null)
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

                    return new ResolvedAnchor(position, cell.Layers);
                }
            }

            return default;
        }

        private static Vector3 NormalizedForward(Vector3 forward)
        {
            return forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        }

        private static WorldTravelResult Result(
            WorldTravelResultStatus status,
            WorldPosition destination,
            string message = null)
        {
            return new WorldTravelResult(status, destination, message);
        }

        private readonly struct ResolvedAnchor
        {
            public ResolvedAnchor(WorldPosition position, WorldCellLayer cellLayers)
            {
                Position = position;
                CellLayers = cellLayers;
            }

            public WorldPosition Position { get; }
            public WorldCellLayer CellLayers { get; }
            public bool HasAnchor => Position.HasAnchor;
        }
    }
}
