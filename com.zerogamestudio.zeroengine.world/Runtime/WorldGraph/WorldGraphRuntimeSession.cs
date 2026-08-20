using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    public sealed class WorldGraphRuntimeSession
    {
        private readonly WorldGraphSO _graph;
        private readonly IWorldCellLoader _loader;
        private readonly IWorldGraphRuntimeActor _actor;
        private readonly IWorldGraphRuntimeLocationStore _locationStore;
        private readonly IWorldCellReadinessService _readinessService;
        private readonly WorldGraphRuntimeSessionOptions _options;
        private readonly Func<DateTimeOffset> _utcNowProvider;

        private WorldStreamingService _streaming;
        private WorldTravelService _travel;
        private WorldNavigationService _navigation;
        private WorldGraphRuntimeSnapshot _snapshot;
        private string _lastFailure;
        private bool _operationInProgress;

        public WorldGraphRuntimeSession(
            WorldGraphSO graph,
            IWorldCellLoader loader,
            IWorldGraphRuntimeActor actor,
            IWorldGraphRuntimeLocationStore locationStore,
            WorldGraphRuntimeSessionOptions options,
            IWorldCellReadinessService readinessService = null,
            Func<DateTimeOffset> utcNowProvider = null)
        {
            _graph = graph;
            _loader = loader;
            _actor = actor;
            _locationStore = locationStore;
            _options = options;
            _readinessService = readinessService;
            _utcNowProvider = utcNowProvider;
            PublishSnapshot("Unloaded");
        }

        public WorldGraphRuntimeSnapshot Snapshot => _snapshot;

        public async Task<WorldGraphRuntimeSessionResult> LoadStartAsync(CancellationToken cancellationToken)
        {
            if (!TryBeginWorldOperation())
            {
                return OperationBusyResult(cancellationToken);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var graphResult = ValidateGraph();
                if (!graphResult.Succeeded)
                {
                    PublishSnapshot("LoadFailed", graphResult.Status.ToString());
                    await CleanupAfterFailureAsync();
                    return graphResult;
                }

                if (!IsExpectedWorldGraph())
                {
                    PublishSnapshot("LoadFailed", WorldGraphRuntimeSessionStatus.GraphMismatch.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.GraphMismatch);
                }

                var startCell = _graph.FindCell(_options.StartCellId);
                if (startCell == null)
                {
                    PublishSnapshot("LoadFailed", WorldGraphRuntimeSessionStatus.StartCellMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.StartCellMissing);
                }

                var startAnchor = _graph.FindAnchor(_options.StartAnchorId);
                if (startAnchor == null || !CellContainsAnchor(startCell, startAnchor))
                {
                    PublishSnapshot("LoadFailed", WorldGraphRuntimeSessionStatus.StartAnchorMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.StartAnchorMissing);
                }

                EnsureRuntimeServices();
                PublishSnapshot("Loading");

                var streamingResult = await _streaming.ActivateCellAsync(
                    startCell.CellId,
                    startCell.Layers,
                    _options.LoadBoundaryCells,
                    cancellationToken);
                if (!streamingResult.Succeeded)
                {
                    PublishSnapshot("LoadFailed", streamingResult.Status.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(MapStreamingStatus(streamingResult.Status), streamingResult);
                }

                SyncNavigationReadyCells(streamingResult);
                var startPosition = ResolveAnchorPosition(startCell, startAnchor);
                if (_actor == null || !_actor.TryPlaceAtAnchor(startCell, startAnchor, startPosition))
                {
                    PublishSnapshot("LoadFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing, streamingResult);
                }

                var location = RecordLocation(startCell, startAnchor, default);
                PublishSnapshot("Exploring");
                return Result(WorldGraphRuntimeSessionStatus.Loaded, streamingResult, default, location);
            }
            catch (OperationCanceledException)
            {
                PublishSnapshot("Cancelled");
                await CleanupAfterFailureAsync();
                return Result(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                PublishSnapshot("Failed", ex.Message);
                await CleanupAfterFailureAsync();
                return Result(WorldGraphRuntimeSessionStatus.Failed, exception: ex);
            }
            finally
            {
                EndWorldOperation();
            }
        }

        public async Task<WorldGraphRuntimeSessionResult> TravelAsync(
            string linkId,
            string fromAnchorId,
            CancellationToken cancellationToken)
        {
            if (!TryBeginWorldOperation())
            {
                return OperationBusyResult(cancellationToken);
            }

            try
            {
                if (_graph == null || _streaming == null)
                {
                    PublishSnapshot("TravelFailed", WorldGraphRuntimeSessionStatus.NotLoaded.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.NotLoaded);
                }

                if (string.IsNullOrWhiteSpace(linkId))
                {
                    PublishSnapshot("TravelFailed", WorldGraphRuntimeSessionStatus.LinkNotFound.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.LinkNotFound);
                }

                if (string.IsNullOrWhiteSpace(fromAnchorId))
                {
                    PublishSnapshot("TravelFailed", WorldGraphRuntimeSessionStatus.AnchorNotFound.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.AnchorNotFound);
                }

                if (_actor == null || !_actor.HasActor)
                {
                    PublishSnapshot("TravelFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing);
                }

                _travel ??= new WorldTravelService(_graph, _streaming);
                PublishSnapshot("TravelPreparing");
                var travelResult = await _travel.TravelAsync(
                    new WorldTravelRequest(linkId, fromAnchorId),
                    cancellationToken);
                if (!travelResult.Succeeded)
                {
                    PublishSnapshot("TravelFailed", $"{travelResult.Status}: {travelResult.Message}");
                    SyncNavigationReadyCells(_streaming.LoadedCellIds);
                    if (HasInvalidStreamingSession())
                    {
                        await CleanupAfterFailureAsync();
                    }

                    return Result(MapTravelStatus(travelResult.Status), default, travelResult);
                }

                SyncNavigationReadyCells(_streaming.LoadedCellIds);
                if (!_actor.TryPlaceAtPosition(travelResult.Destination))
                {
                    PublishSnapshot("TravelFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing, default, travelResult);
                }

                var cell = _graph.FindCell(travelResult.Destination.CellId);
                var anchor = _graph.FindAnchor(travelResult.Destination.AnchorId);
                var location = RecordLocation(cell, anchor, default);
                PublishSnapshot("Exploring");
                return Result(WorldGraphRuntimeSessionStatus.Traveled, default, travelResult, location);
            }
            catch (OperationCanceledException)
            {
                SyncNavigationReadyCells(_streaming?.LoadedCellIds);
                PublishSnapshot("Cancelled");
                return Result(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                SyncNavigationReadyCells(_streaming?.LoadedCellIds);
                PublishSnapshot("TravelFailed", ex.Message);
                return Result(WorldGraphRuntimeSessionStatus.Failed, exception: ex);
            }
            finally
            {
                EndWorldOperation();
            }
        }

        public async Task<WorldGraphRuntimeSessionResult> ActivateCellAsync(
            string sourceCellId,
            string targetCellId,
            string boundaryId,
            CancellationToken cancellationToken)
        {
            if (!TryBeginWorldOperation())
            {
                return OperationBusyResult(cancellationToken);
            }

            try
            {
                if (_graph == null || _streaming == null)
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.NotLoaded.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.NotLoaded);
                }

                if (!string.IsNullOrWhiteSpace(sourceCellId) && _streaming.ActiveCellId != sourceCellId)
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.ActiveCellMismatch.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.ActiveCellMismatch);
                }

                if (!IsAuthorizedStreamingBoundary(sourceCellId, targetCellId, boundaryId))
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.StreamingBoundaryMissing.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.StreamingBoundaryMissing);
                }

                if (_actor == null || !_actor.HasActor)
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing);
                }

                var targetCell = _graph.FindCell(targetCellId);
                if (targetCell == null)
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.TargetCellMissing.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.TargetCellMissing);
                }

                PublishSnapshot("StreamingActivating");
                var streamingResult = await _streaming.ActivateCellAsync(
                    targetCell.CellId,
                    targetCell.Layers,
                    _options.LoadBoundaryCells,
                    cancellationToken);
                if (!streamingResult.Succeeded)
                {
                    PublishSnapshot("StreamingFailed", streamingResult.Status.ToString());
                    SyncNavigationReadyCells(_streaming.LoadedCellIds);
                    if (HasInvalidStreamingSession())
                    {
                        await CleanupAfterFailureAsync();
                    }

                    return Result(MapStreamingStatus(streamingResult.Status), streamingResult);
                }

                SyncNavigationReadyCells(streamingResult);
                if (!_actor.HasActor)
                {
                    PublishSnapshot("StreamingFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing, streamingResult);
                }

                var location = RecordLocation(targetCell, null, default);
                PublishSnapshot("Exploring");
                return Result(WorldGraphRuntimeSessionStatus.Loaded, streamingResult, default, location);
            }
            catch (OperationCanceledException)
            {
                SyncNavigationReadyCells(_streaming?.LoadedCellIds);
                PublishSnapshot("Cancelled");
                return Result(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                SyncNavigationReadyCells(_streaming?.LoadedCellIds);
                PublishSnapshot("StreamingFailed", ex.Message);
                return Result(WorldGraphRuntimeSessionStatus.Failed, exception: ex);
            }
            finally
            {
                EndWorldOperation();
            }
        }

        public async Task<WorldGraphRuntimeSessionResult> RestoreAsync(
            WorldGraphRuntimeLocation location,
            CancellationToken cancellationToken)
        {
            if (!TryBeginWorldOperation())
            {
                return OperationBusyResult(cancellationToken);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var graphResult = ValidateGraph();
                if (!graphResult.Succeeded)
                {
                    PublishSnapshot("RestoreFailed", graphResult.Status.ToString());
                    return graphResult;
                }

                if (!location.IsValid || !string.Equals(location.WorldGraphId, _graph.WorldGraphId, StringComparison.Ordinal))
                {
                    PublishSnapshot("RestoreFailed", WorldGraphRuntimeSessionStatus.GraphMismatch.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.GraphMismatch);
                }

                if (_actor == null || !_actor.HasActor)
                {
                    PublishSnapshot("RestoreFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing);
                }

                var targetCell = _graph.FindCell(location.CellId);
                if (targetCell == null)
                {
                    PublishSnapshot("RestoreFailed", WorldGraphRuntimeSessionStatus.TargetCellMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.TargetCellMissing);
                }

                EnsureRuntimeServices();
                PublishSnapshot("Restoring");

                var streamingResult = await _streaming.ActivateCellAsync(
                    targetCell.CellId,
                    targetCell.Layers,
                    _options.LoadBoundaryCells,
                    cancellationToken);
                if (!streamingResult.Succeeded)
                {
                    PublishSnapshot("RestoreFailed", streamingResult.Status.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(MapStreamingStatus(streamingResult.Status), streamingResult);
                }

                SyncNavigationReadyCells(streamingResult);
                if (!_actor.TryPlaceAtLocation(targetCell, location))
                {
                    PublishSnapshot("RestoreFailed", WorldGraphRuntimeSessionStatus.ActorMissing.ToString());
                    await CleanupAfterFailureAsync();
                    return Result(WorldGraphRuntimeSessionStatus.ActorMissing, streamingResult);
                }

                var recordedLocation = RecordLocation(targetCell, _graph.FindAnchor(location.AnchorId), location);
                PublishSnapshot("Exploring");
                return Result(WorldGraphRuntimeSessionStatus.Loaded, streamingResult, default, recordedLocation);
            }
            catch (OperationCanceledException)
            {
                PublishSnapshot("Cancelled");
                await CleanupAfterFailureAsync();
                return Result(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                PublishSnapshot("RestoreFailed", ex.Message);
                await CleanupAfterFailureAsync();
                return Result(WorldGraphRuntimeSessionStatus.Failed, exception: ex);
            }
            finally
            {
                EndWorldOperation();
            }
        }

        public async Task<WorldGraphRuntimeSessionResult> UnloadAsync(CancellationToken cancellationToken)
        {
            if (!TryBeginWorldOperation())
            {
                return OperationBusyResult(cancellationToken);
            }

            try
            {
                return await UnloadInternalAsync(cancellationToken, publishSnapshot: true);
            }
            finally
            {
                EndWorldOperation();
            }
        }

        private async Task<WorldGraphRuntimeSessionResult> CleanupAfterFailureAsync()
        {
            return await UnloadInternalAsync(CancellationToken.None, publishSnapshot: false);
        }

        private async Task<WorldGraphRuntimeSessionResult> UnloadInternalAsync(
            CancellationToken cancellationToken,
            bool publishSnapshot)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unloadResult = await UnloadLoadedCellsIgnoringCancellationAsync();
                if (unloadResult.Status == WorldCellOperationStatus.Cancelled)
                {
                    return Result(WorldGraphRuntimeSessionStatus.Cancelled);
                }

                if (!unloadResult.IsSuccess)
                {
                    var message = string.IsNullOrWhiteSpace(unloadResult.Message)
                        ? "World graph unload failed."
                        : unloadResult.Message;
                    return Result(
                        WorldGraphRuntimeSessionStatus.UnloadFailed,
                        exception: new InvalidOperationException(message));
                }

                ResetRuntimeServices();
                _locationStore?.Clear();
                if (publishSnapshot)
                {
                    PublishSnapshot("Unloaded");
                }

                return Result(WorldGraphRuntimeSessionStatus.Unloaded);
            }
            catch (OperationCanceledException)
            {
                return Result(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                return Result(WorldGraphRuntimeSessionStatus.UnloadFailed, exception: ex);
            }
        }

        private async Task<WorldCellOperationResult> UnloadLoadedCellsIgnoringCancellationAsync()
        {
            if (_loader == null)
            {
                return WorldCellOperationResult.SucceededResult(null);
            }

            if (_graph == null || _streaming == null || _streaming.LoadedCellIds.Count == 0)
            {
                return WorldCellOperationResult.SucceededResult(null);
            }

            if (_loader is IWorldCellBulkUnloader bulkUnloader)
            {
                return await bulkUnloader.UnloadAllAsync(CancellationToken.None);
            }

            foreach (var cellId in _streaming.LoadedCellIds.ToArray())
            {
                var cell = _graph.FindCell(cellId);
                if (cell == null)
                {
                    continue;
                }

                var unloadResult = await _loader.UnloadCellAsync(cell, CancellationToken.None);
                if (!unloadResult.IsSuccess)
                {
                    return unloadResult;
                }
            }

            return WorldCellOperationResult.SucceededResult(null);
        }

        private void EnsureRuntimeServices()
        {
            _streaming ??= new WorldStreamingService(
                _graph,
                _loader,
                _options.MaxLoadedBudgetWeight,
                _options.MinimumCellResidency,
                _utcNowProvider,
                _readinessService);
            _travel ??= new WorldTravelService(_graph, _streaming);
            _navigation ??= new WorldNavigationService(_graph);
        }

        private void ResetRuntimeServices()
        {
            _streaming = null;
            _travel = null;
            _navigation = null;
        }

        private WorldGraphRuntimeSessionResult ValidateGraph()
        {
            if (_graph == null)
            {
                return Result(WorldGraphRuntimeSessionStatus.GraphMissing);
            }

            if (_loader == null)
            {
                return Result(WorldGraphRuntimeSessionStatus.StreamingFailed);
            }

            return Result(WorldGraphRuntimeSessionStatus.Loaded);
        }

        private bool IsExpectedWorldGraph()
        {
            return string.IsNullOrWhiteSpace(_options.ExpectedWorldGraphId)
                   || string.Equals(_graph.WorldGraphId, _options.ExpectedWorldGraphId, StringComparison.Ordinal);
        }

        private bool IsAuthorizedStreamingBoundary(
            string sourceCellId,
            string targetCellId,
            string boundaryId)
        {
            if (string.IsNullOrWhiteSpace(sourceCellId)
                || string.IsNullOrWhiteSpace(targetCellId)
                || string.IsNullOrWhiteSpace(boundaryId))
            {
                return false;
            }

            var sourceCell = _graph?.FindCell(sourceCellId);
            if (sourceCell == null)
            {
                return false;
            }

            foreach (var boundary in sourceCell.StreamingBoundaries)
            {
                if (boundary == null
                    || boundary.BoundaryId != boundaryId
                    || boundary.TargetCellIds == null)
                {
                    continue;
                }

                foreach (var boundaryTargetCellId in boundary.TargetCellIds)
                {
                    if (boundaryTargetCellId == targetCellId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SyncNavigationReadyCells(WorldStreamingResult streamingResult)
        {
            _navigation?.ReplaceReadyCells(streamingResult.LoadedCellIds);
        }

        private void SyncNavigationReadyCells(IReadOnlyList<string> loadedCellIds)
        {
            _navigation?.ReplaceReadyCells(loadedCellIds ?? Array.Empty<string>());
        }

        private bool HasInvalidStreamingSession()
        {
            return _streaming != null
                   && string.IsNullOrWhiteSpace(_streaming.ActiveCellId)
                   && _streaming.LoadedCellIds.Count > 0;
        }

        private WorldGraphRuntimeLocation RecordLocation(
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor,
            WorldGraphRuntimeLocation fallback)
        {
            if (_graph == null || cell == null)
            {
                return default;
            }

            var fallbackLocation = BuildFallbackLocation(cell, anchor, fallback);
            var location = _actor?.CaptureLocation(_graph, cell, anchor, fallbackLocation) ?? fallbackLocation;
            if (!location.IsValid)
            {
                location = fallbackLocation;
            }

            _locationStore?.Save(location);
            return location;
        }

        private WorldGraphRuntimeLocation BuildFallbackLocation(
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor,
            WorldGraphRuntimeLocation fallback)
        {
            var hasFallback = fallback.IsValid;
            var anchorRotation = anchor != null
                ? Quaternion.LookRotation(NormalizedForward(anchor.CellLocalForward), Vector3.up)
                : Quaternion.identity;
            var cellLocalPosition = hasFallback ? fallback.CellLocalPosition : anchor?.CellLocalPosition ?? Vector3.zero;
            var cellLocalRotation = hasFallback ? fallback.CellLocalRotation : anchorRotation;
            var worldPosition = hasFallback ? fallback.WorldPosition : cell.WorldOrigin + cellLocalPosition;
            var worldRotation = hasFallback ? fallback.WorldRotation : cellLocalRotation;
            var locationName = ResolveLocationName(cell, anchor, fallback);

            return new WorldGraphRuntimeLocation(
                _graph.WorldGraphId,
                ResolveRegionId(cell.CellId),
                cell.CellId,
                anchor?.AnchorId ?? fallback.AnchorId,
                locationName,
                cell.WorldOrigin,
                cellLocalPosition,
                cellLocalRotation,
                worldPosition,
                worldRotation);
        }

        private WorldPosition ResolveAnchorPosition(
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor)
        {
            var rotation = Quaternion.LookRotation(
                NormalizedForward(anchor.CellLocalForward),
                Vector3.up);
            return new WorldPosition(
                _graph.WorldGraphId,
                ResolveRegionId(cell.CellId),
                cell.CellId,
                anchor.AnchorId,
                anchor.CellLocalPosition,
                rotation,
                cell.WorldOrigin + anchor.CellLocalPosition,
                rotation);
        }

        private string ResolveRegionId(string cellId)
        {
            if (_graph == null)
            {
                return string.Empty;
            }

            foreach (var region in _graph.Regions)
            {
                if (region == null)
                {
                    continue;
                }

                foreach (var cell in region.Cells)
                {
                    if (cell != null && cell.CellId == cellId)
                    {
                        return region.RegionId;
                    }
                }
            }

            return string.Empty;
        }

        private static string ResolveLocationName(
            WorldCellDefinition cell,
            WorldAnchorDefinition anchor,
            WorldGraphRuntimeLocation fallback)
        {
            if (!string.IsNullOrWhiteSpace(anchor?.DisplayName))
            {
                return anchor.DisplayName;
            }

            if (!string.IsNullOrWhiteSpace(cell.DisplayName))
            {
                return cell.DisplayName;
            }

            return string.IsNullOrWhiteSpace(fallback.LocationName)
                ? cell.CellId
                : fallback.LocationName;
        }

        private void PublishSnapshot(string runtimeState, string failure = null)
        {
            if (!string.IsNullOrWhiteSpace(failure))
            {
                _lastFailure = failure;
            }

            var loadedCellIds = _streaming?.LoadedCellIds ?? Array.Empty<string>();
            _snapshot = new WorldGraphRuntimeSnapshot(
                _graph?.WorldGraphId,
                _streaming?.ActiveCellId,
                loadedCellIds,
                BuildPinnedCellSummaries(loadedCellIds),
                runtimeState,
                _lastFailure);
        }

        private IReadOnlyList<string> BuildPinnedCellSummaries(IReadOnlyList<string> loadedCellIds)
        {
            if (_streaming == null || loadedCellIds == null || loadedCellIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var summaries = new List<string>();
            foreach (var cellId in loadedCellIds)
            {
                var reasons = _streaming.GetCellPinReasons(cellId);
                if (reasons.Count == 0)
                {
                    continue;
                }

                summaries.Add($"{cellId}: {string.Join(", ", reasons)}");
            }

            return summaries;
        }

        private bool TryBeginWorldOperation()
        {
            if (_operationInProgress)
            {
                return false;
            }

            _operationInProgress = true;
            return true;
        }

        private void EndWorldOperation()
        {
            _operationInProgress = false;
        }

        private static bool CellContainsAnchor(WorldCellDefinition cell, WorldAnchorDefinition anchor)
        {
            if (cell == null || anchor == null)
            {
                return false;
            }

            foreach (var cellAnchor in cell.Anchors)
            {
                if (cellAnchor != null && cellAnchor.AnchorId == anchor.AnchorId)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 NormalizedForward(Vector3 forward)
        {
            return forward.sqrMagnitude > 0f ? forward.normalized : Vector3.forward;
        }

        private static WorldGraphRuntimeSessionStatus MapStreamingStatus(WorldStreamingResultStatus status)
        {
            return status switch
            {
                WorldStreamingResultStatus.Busy => WorldGraphRuntimeSessionStatus.Busy,
                WorldStreamingResultStatus.Cancelled => WorldGraphRuntimeSessionStatus.Cancelled,
                WorldStreamingResultStatus.CellNotFound => WorldGraphRuntimeSessionStatus.TargetCellMissing,
                _ => WorldGraphRuntimeSessionStatus.StreamingFailed
            };
        }

        private static WorldGraphRuntimeSessionStatus MapTravelStatus(WorldTravelResultStatus status)
        {
            return status switch
            {
                WorldTravelResultStatus.LinkNotFound => WorldGraphRuntimeSessionStatus.LinkNotFound,
                WorldTravelResultStatus.AnchorNotFound => WorldGraphRuntimeSessionStatus.AnchorNotFound,
                WorldTravelResultStatus.OriginMismatch => WorldGraphRuntimeSessionStatus.OriginMismatch,
                WorldTravelResultStatus.StreamingFailed => WorldGraphRuntimeSessionStatus.StreamingFailed,
                WorldTravelResultStatus.Busy => WorldGraphRuntimeSessionStatus.Busy,
                WorldTravelResultStatus.Cancelled => WorldGraphRuntimeSessionStatus.Cancelled,
                _ => WorldGraphRuntimeSessionStatus.TravelFailed
            };
        }

        private WorldGraphRuntimeSessionResult OperationBusyResult(CancellationToken cancellationToken)
        {
            return cancellationToken.IsCancellationRequested
                ? Result(WorldGraphRuntimeSessionStatus.Cancelled)
                : Result(WorldGraphRuntimeSessionStatus.Busy);
        }

        private static WorldGraphRuntimeSessionResult Result(
            WorldGraphRuntimeSessionStatus status,
            WorldStreamingResult streamingResult = default,
            WorldTravelResult travelResult = default,
            WorldGraphRuntimeLocation location = default,
            Exception exception = null)
        {
            return new WorldGraphRuntimeSessionResult(
                status,
                streamingResult,
                travelResult,
                location,
                exception);
        }
    }
}
