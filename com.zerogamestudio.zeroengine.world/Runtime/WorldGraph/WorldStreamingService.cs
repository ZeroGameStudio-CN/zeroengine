using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.WorldGraph
{
    public sealed class WorldStreamingService
    {
        private readonly WorldGraphSO _graph;
        private readonly IWorldCellLoader _loader;
        private readonly HashSet<string> _loadedCellSet = new HashSet<string>();
        private readonly Dictionary<string, Dictionary<int, string>> _pinReasonsByCell =
            new Dictionary<string, Dictionary<int, string>>();
        private readonly Dictionary<string, DateTimeOffset> _loadedAtUtcByCell =
            new Dictionary<string, DateTimeOffset>();
        private readonly List<string> _loadedCellIds = new List<string>();
        private readonly int _maxLoadedBudgetWeight;
        private readonly TimeSpan _minimumCellResidency;
        private readonly Func<DateTimeOffset> _utcNowProvider;
        private readonly IWorldCellReadinessService _readinessService;
        private int _nextPinId;
        private bool _operationInProgress;

        public WorldStreamingService(
            WorldGraphSO graph,
            IWorldCellLoader loader,
            int maxLoadedBudgetWeight = int.MaxValue)
            : this(
                graph,
                loader,
                maxLoadedBudgetWeight,
                TimeSpan.Zero,
                null)
        {
        }

        public WorldStreamingService(
            WorldGraphSO graph,
            IWorldCellLoader loader,
            int maxLoadedBudgetWeight,
            TimeSpan minimumCellResidency,
            Func<DateTimeOffset> utcNowProvider = null,
            IWorldCellReadinessService readinessService = null)
        {
            _graph = graph;
            _loader = loader;
            _maxLoadedBudgetWeight = maxLoadedBudgetWeight <= 0 ? int.MaxValue : maxLoadedBudgetWeight;
            _minimumCellResidency = minimumCellResidency <= TimeSpan.Zero
                ? TimeSpan.Zero
                : minimumCellResidency;
            _utcNowProvider = utcNowProvider;
            _readinessService = readinessService;
        }

        public string ActiveCellId { get; private set; }
        public IReadOnlyList<string> LoadedCellIds => _loadedCellIds;

        public bool IsCellLoaded(string cellId)
        {
            return !string.IsNullOrWhiteSpace(cellId) && _loadedCellSet.Contains(cellId);
        }

        public bool IsCellPinned(string cellId)
        {
            return !string.IsNullOrWhiteSpace(cellId)
                   && _pinReasonsByCell.TryGetValue(cellId, out var pins)
                   && pins.Count > 0;
        }

        public void SetCellPinned(string cellId, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(cellId))
            {
                return;
            }

            if (pinned)
            {
                EnsurePinMap(cellId)[0] = "manual";
                return;
            }

            ReleaseCellPin(cellId, 0);
        }

        public IDisposable AcquireCellPin(string cellId, string reason)
        {
            if (string.IsNullOrWhiteSpace(cellId))
            {
                return WorldCellResidencyHandle.Empty;
            }

            var pinId = ++_nextPinId;
            EnsurePinMap(cellId)[pinId] = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
            return new WorldCellResidencyHandle(this, cellId, pinId);
        }

        public IReadOnlyList<string> GetCellPinReasons(string cellId)
        {
            if (string.IsNullOrWhiteSpace(cellId)
                || !_pinReasonsByCell.TryGetValue(cellId, out var pins)
                || pins.Count == 0)
            {
                return Array.Empty<string>();
            }

            var reasons = new List<string>(pins.Count);
            foreach (var reason in pins.Values)
            {
                reasons.Add(reason);
            }

            return reasons;
        }

        public async Task<WorldStreamingResult> ActivateCellAsync(
            string cellId,
            WorldCellLayer layers,
            bool loadBoundaryCells,
            CancellationToken cancellationToken)
        {
            if (_operationInProgress)
            {
                return Result(WorldStreamingResultStatus.Busy, "A world streaming operation is already in progress.");
            }

            _operationInProgress = true;
            try
            {
                var windowResult = BuildRequiredWindow(cellId, loadBoundaryCells, out var requiredCells);
                if (!windowResult.Succeeded)
                {
                    return windowResult;
                }

                if (!FitsLoadedBudget(requiredCells))
                {
                    return Result(WorldStreamingResultStatus.BudgetExceeded, $"World streaming window for '{cellId}' exceeds loaded budget {_maxLoadedBudgetWeight}.");
                }

                if (!FitsLoadedBudgetWithProtectedResidents(requiredCells))
                {
                    return Result(WorldStreamingResultStatus.BudgetExceeded, $"World streaming window for '{cellId}' cannot fit loaded budget {_maxLoadedBudgetWeight} until protected resident cells can unload.");
                }

                var loadedBefore = new HashSet<string>(_loadedCellSet);
                var newlyLoadedCellIds = new List<string>();
                var requiredCellIds = new HashSet<string>();
                foreach (var cell in requiredCells)
                {
                    var requestedLayers = string.Equals(cell.CellId, cellId, StringComparison.Ordinal)
                        ? layers
                        : cell.Layers;
                    var loadResult = await EnsureCellLoadedInternalAsync(cell.CellId, requestedLayers, cancellationToken);
                    if (!loadResult.Succeeded)
                    {
                        await RollbackNewlyLoadedCellsAsync(newlyLoadedCellIds);
                        return loadResult;
                    }

                    if (!loadedBefore.Contains(cell.CellId))
                    {
                        newlyLoadedCellIds.Add(cell.CellId);
                    }

                    requiredCellIds.Add(cell.CellId);
                }

                var unloadResult = await UnloadCellsOutsideWindowAsync(requiredCellIds, cancellationToken);
                if (!unloadResult.Succeeded)
                {
                    if (unloadResult.Status == WorldStreamingResultStatus.Cancelled)
                    {
                        await RollbackNewlyLoadedCellsAsync(newlyLoadedCellIds);
                    }

                    return unloadResult;
                }

                ActiveCellId = cellId;
                return Result(WorldStreamingResultStatus.Succeeded);
            }
            finally
            {
                _operationInProgress = false;
            }
        }

        public async Task<WorldStreamingResult> EnsureCellLoadedAsync(
            string cellId,
            WorldCellLayer layers,
            CancellationToken cancellationToken)
        {
            if (_operationInProgress)
            {
                return Result(WorldStreamingResultStatus.Busy, "A world streaming operation is already in progress.");
            }

            _operationInProgress = true;
            try
            {
                return await EnsureCellLoadedInternalAsync(cellId, layers, cancellationToken);
            }
            finally
            {
                _operationInProgress = false;
            }
        }

        private async Task<WorldStreamingResult> EnsureCellLoadedInternalAsync(
            string cellId,
            WorldCellLayer layers,
            CancellationToken cancellationToken)
        {
            if (_graph == null)
            {
                return Result(WorldStreamingResultStatus.GraphMissing, "WorldGraph is missing.");
            }

            if (_loader == null)
            {
                return Result(WorldStreamingResultStatus.LoaderFailed, "World cell loader is missing.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Result(WorldStreamingResultStatus.Cancelled, "World cell load was cancelled.");
            }

            if (IsCellLoaded(cellId))
            {
                return Result(WorldStreamingResultStatus.Succeeded);
            }

            var cell = _graph.FindCell(cellId);
            if (cell == null)
            {
                return Result(WorldStreamingResultStatus.CellNotFound, $"World cell '{cellId}' was not found.");
            }

            try
            {
                var resolvedLayers = ResolveLayerMask(cell, layers);
                var operation = await _loader.LoadCellAsync(cell, resolvedLayers, cancellationToken);
                if (operation.Status == WorldCellOperationStatus.Cancelled)
                {
                    return Result(WorldStreamingResultStatus.Cancelled, operation.Message);
                }

                if (!operation.IsSuccess)
                {
                    return Result(WorldStreamingResultStatus.LoaderFailed, operation.Message);
                }

                var readinessResult = await PrepareCellReadinessAsync(cell, resolvedLayers, cancellationToken);
                if (!readinessResult.IsSuccess)
                {
                    await TryUnloadCellAfterReadinessFailureAsync(cell);
                    return readinessResult.Status == WorldCellReadinessStatus.Cancelled
                        ? Result(WorldStreamingResultStatus.Cancelled, readinessResult.Message)
                        : Result(WorldStreamingResultStatus.ReadinessFailed, readinessResult.Message);
                }
            }
            catch (OperationCanceledException)
            {
                await TryUnloadCellAfterReadinessFailureAsync(cell);
                return Result(WorldStreamingResultStatus.Cancelled, "World cell load was cancelled.");
            }
            catch (Exception ex)
            {
                return Result(WorldStreamingResultStatus.LoaderFailed, ex.Message);
            }

            if (_loadedCellSet.Add(cellId))
            {
                _loadedCellIds.Add(cellId);
                _loadedAtUtcByCell[cellId] = UtcNow();
            }

            return Result(WorldStreamingResultStatus.Succeeded);
        }

        private async Task<WorldCellReadinessResult> PrepareCellReadinessAsync(
            WorldCellDefinition cell,
            WorldCellLayer layers,
            CancellationToken cancellationToken)
        {
            if (_readinessService == null)
            {
                return WorldCellReadinessResult.SucceededResult(cell.CellId);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return WorldCellReadinessResult.Cancelled(cell.CellId, "World cell readiness was cancelled.");
            }

            try
            {
                return await _readinessService.PrepareCellAsync(cell, layers, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return WorldCellReadinessResult.Cancelled(cell.CellId, "World cell readiness was cancelled.");
            }
            catch (Exception ex)
            {
                return WorldCellReadinessResult.Failed(cell.CellId, ex.Message);
            }
        }

        private async Task TryUnloadCellAfterReadinessFailureAsync(WorldCellDefinition cell)
        {
            if (_loader == null || cell == null)
            {
                return;
            }

            try
            {
                await _loader.UnloadCellAsync(cell, CancellationToken.None);
            }
            catch (Exception)
            {
                // Keep the original readiness failure as the reported cause.
            }
        }

        private async Task RollbackNewlyLoadedCellsAsync(IReadOnlyList<string> newlyLoadedCellIds)
        {
            if (_loader == null || newlyLoadedCellIds.Count == 0)
            {
                return;
            }

            for (var i = newlyLoadedCellIds.Count - 1; i >= 0; i--)
            {
                var cellId = newlyLoadedCellIds[i];
                var cell = _graph.FindCell(cellId);
                if (cell == null)
                {
                    RemoveLoadedCell(cellId);
                    continue;
                }

                try
                {
                    var operation = await _loader.UnloadCellAsync(cell, CancellationToken.None);
                    if (operation.IsSuccess)
                    {
                        RemoveLoadedCell(cellId);
                    }
                }
                catch (Exception)
                {
                    // Rollback is best-effort; keep state so a later window change can retry unload.
                }
            }
        }

        private WorldStreamingResult BuildRequiredWindow(
            string cellId,
            bool loadBoundaryCells,
            out List<WorldCellDefinition> requiredCells)
        {
            requiredCells = new List<WorldCellDefinition>();

            if (_graph == null)
            {
                return Result(WorldStreamingResultStatus.GraphMissing, "WorldGraph is missing.");
            }

            var activeCell = _graph.FindCell(cellId);
            if (activeCell == null)
            {
                return Result(WorldStreamingResultStatus.CellNotFound, $"World cell '{cellId}' was not found.");
            }

            AddRequiredCell(requiredCells, activeCell);

            if (loadBoundaryCells)
            {
                foreach (var boundary in activeCell.StreamingBoundaries)
                {
                    if (boundary == null)
                    {
                        continue;
                    }

                    foreach (var targetCellId in boundary.TargetCellIds)
                    {
                        var targetCell = _graph.FindCell(targetCellId);
                        if (targetCell == null)
                        {
                            return Result(WorldStreamingResultStatus.CellNotFound, $"World boundary target cell '{targetCellId}' was not found.");
                        }

                        AddRequiredCell(requiredCells, targetCell);
                    }
                }

                foreach (var linkedCell in FindSeamlessWalkLinkedCells(activeCell))
                {
                    AddRequiredCell(requiredCells, linkedCell);
                }
            }

            foreach (var pinnedCellId in _pinReasonsByCell.Keys)
            {
                if (!IsCellLoaded(pinnedCellId))
                {
                    continue;
                }

                var pinnedCell = _graph.FindCell(pinnedCellId);
                if (pinnedCell != null)
                {
                    AddRequiredCell(requiredCells, pinnedCell);
                }
            }

            return Result(WorldStreamingResultStatus.Succeeded);
        }

        private IEnumerable<WorldCellDefinition> FindSeamlessWalkLinkedCells(WorldCellDefinition activeCell)
        {
            foreach (var link in _graph.TravelLinks)
            {
                if (link == null || link.TravelMode != WorldTravelMode.SeamlessWalk)
                {
                    continue;
                }

                var fromCell = FindCellContainingAnchor(link.FromAnchorId);
                var toCell = FindCellContainingAnchor(link.ToAnchorId);
                if (fromCell == null || toCell == null)
                {
                    continue;
                }

                if (fromCell.CellId == activeCell.CellId)
                {
                    yield return toCell;
                    continue;
                }

                if (link.Bidirectional && toCell.CellId == activeCell.CellId)
                {
                    yield return fromCell;
                }
            }
        }

        private WorldCellDefinition FindCellContainingAnchor(string anchorId)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                return null;
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
                        if (anchor != null && anchor.AnchorId == anchorId)
                        {
                            return cell;
                        }
                    }
                }
            }

            return null;
        }

        private async Task<WorldStreamingResult> UnloadCellsOutsideWindowAsync(
            HashSet<string> requiredCellIds,
            CancellationToken cancellationToken)
        {
            if (_loader == null)
            {
                return Result(WorldStreamingResultStatus.LoaderFailed, "World cell loader is missing.");
            }

            var loadedSnapshot = _loadedCellIds.ToArray();
            foreach (var loadedCellId in loadedSnapshot)
            {
                if (requiredCellIds.Contains(loadedCellId) || IsProtectedResidentCell(loadedCellId))
                {
                    continue;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return Result(WorldStreamingResultStatus.Cancelled, "World cell unload was cancelled.");
                }

                var loadedCell = _graph.FindCell(loadedCellId);
                if (loadedCell == null)
                {
                    RemoveLoadedCell(loadedCellId);
                    continue;
                }

                WorldCellOperationResult operation;
                try
                {
                    operation = await _loader.UnloadCellAsync(loadedCell, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return Result(WorldStreamingResultStatus.Cancelled, "World cell unload was cancelled.");
                }
                catch (Exception ex)
                {
                    return Result(WorldStreamingResultStatus.LoaderFailed, ex.Message);
                }

                if (operation.Status == WorldCellOperationStatus.Cancelled)
                {
                    return Result(WorldStreamingResultStatus.Cancelled, operation.Message);
                }

                if (!operation.IsSuccess)
                {
                    return Result(WorldStreamingResultStatus.LoaderFailed, operation.Message);
                }

                RemoveLoadedCell(loadedCellId);
            }

            return Result(WorldStreamingResultStatus.Succeeded);
        }

        private bool FitsLoadedBudget(IReadOnlyList<WorldCellDefinition> requiredCells)
        {
            var totalBudget = 0;
            foreach (var cell in requiredCells)
            {
                totalBudget += Math.Max(1, cell.BudgetWeight);
                if (totalBudget > _maxLoadedBudgetWeight)
                {
                    return false;
                }
            }

            return true;
        }

        private bool FitsLoadedBudgetWithProtectedResidents(IReadOnlyList<WorldCellDefinition> requiredCells)
        {
            var retainedCellIds = new HashSet<string>();
            var totalBudget = 0;

            foreach (var cell in requiredCells)
            {
                if (retainedCellIds.Add(cell.CellId))
                {
                    totalBudget += Math.Max(1, cell.BudgetWeight);
                    if (totalBudget > _maxLoadedBudgetWeight)
                    {
                        return false;
                    }
                }
            }

            foreach (var loadedCellId in _loadedCellIds)
            {
                if (retainedCellIds.Contains(loadedCellId) || !IsProtectedResidentCell(loadedCellId))
                {
                    continue;
                }

                var loadedCell = _graph.FindCell(loadedCellId);
                if (loadedCell == null)
                {
                    continue;
                }

                retainedCellIds.Add(loadedCellId);
                totalBudget += Math.Max(1, loadedCell.BudgetWeight);
                if (totalBudget > _maxLoadedBudgetWeight)
                {
                    return false;
                }
            }

            return true;
        }

        private static WorldCellLayer ResolveLayerMask(
            WorldCellDefinition cell,
            WorldCellLayer requestedLayers)
        {
            var authoredLayers = cell.Layers == WorldCellLayer.None ? WorldCellLayer.All : cell.Layers;
            if (requestedLayers == WorldCellLayer.None || requestedLayers == WorldCellLayer.All)
            {
                return authoredLayers;
            }

            var requestedAuthoredLayers = authoredLayers & requestedLayers;
            return requestedAuthoredLayers == WorldCellLayer.None ? authoredLayers : requestedAuthoredLayers;
        }

        private static void AddRequiredCell(
            List<WorldCellDefinition> requiredCells,
            WorldCellDefinition cell)
        {
            foreach (var existing in requiredCells)
            {
                if (existing.CellId == cell.CellId)
                {
                    return;
                }
            }

            requiredCells.Add(cell);
        }

        private void RemoveLoadedCell(string cellId)
        {
            _loadedCellSet.Remove(cellId);
            _loadedCellIds.Remove(cellId);
            _loadedAtUtcByCell.Remove(cellId);
            if (ActiveCellId == cellId)
            {
                ActiveCellId = null;
            }
        }

        private bool IsProtectedResidentCell(string cellId)
        {
            return IsCellPinned(cellId) || IsInsideMinimumResidency(cellId);
        }

        private bool IsInsideMinimumResidency(string cellId)
        {
            if (_minimumCellResidency == TimeSpan.Zero
                || !_loadedAtUtcByCell.TryGetValue(cellId, out var loadedAtUtc))
            {
                return false;
            }

            return UtcNow() - loadedAtUtc < _minimumCellResidency;
        }

        private DateTimeOffset UtcNow()
        {
            return _utcNowProvider != null ? _utcNowProvider() : DateTimeOffset.UtcNow;
        }

        private Dictionary<int, string> EnsurePinMap(string cellId)
        {
            if (_pinReasonsByCell.TryGetValue(cellId, out var pins))
            {
                return pins;
            }

            pins = new Dictionary<int, string>();
            _pinReasonsByCell[cellId] = pins;
            return pins;
        }

        private void ReleaseCellPin(string cellId, int pinId)
        {
            if (!_pinReasonsByCell.TryGetValue(cellId, out var pins))
            {
                return;
            }

            pins.Remove(pinId);
            if (pins.Count == 0)
            {
                _pinReasonsByCell.Remove(cellId);
            }
        }

        private WorldStreamingResult Result(WorldStreamingResultStatus status, string message = null)
        {
            return new WorldStreamingResult(status, ActiveCellId, _loadedCellIds.ToArray(), message);
        }

        private sealed class WorldCellResidencyHandle : IDisposable
        {
            public static readonly IDisposable Empty = new WorldCellResidencyHandle(null, null, -1);

            private WorldStreamingService _owner;
            private readonly string _cellId;
            private readonly int _pinId;

            public WorldCellResidencyHandle(WorldStreamingService owner, string cellId, int pinId)
            {
                _owner = owner;
                _cellId = cellId;
                _pinId = pinId;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.ReleaseCellPin(_cellId, _pinId);
            }
        }
    }
}
