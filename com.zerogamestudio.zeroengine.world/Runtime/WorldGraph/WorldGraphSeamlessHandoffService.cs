using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZeroEngine.World.WorldGraph
{
    public sealed class WorldGraphSeamlessHandoffService
    {
        private readonly WorldGraphConnectionNetworkSO _network;
        private readonly IWorldGraphRuntimeHost _host;
        private bool _operationInProgress;

        public WorldGraphSeamlessHandoffService(
            WorldGraphConnectionNetworkSO network,
            IWorldGraphRuntimeHost host)
        {
            _network = network;
            _host = host;
        }

        public async Task<WorldGraphHandoffResult> HandoffAsync(
            WorldGraphHandoffRequest request,
            CancellationToken cancellationToken)
        {
            if (_operationInProgress)
            {
                return new WorldGraphHandoffResult(WorldGraphRuntimeSessionStatus.Busy);
            }

            _operationInProgress = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_network == null || _host == null)
                {
                    return new WorldGraphHandoffResult(WorldGraphRuntimeSessionStatus.HandoffConnectionMissing);
                }

                if (!_network.TryFindByBoundary(
                        request.SourceWorldGraphId,
                        request.SourceCellId,
                        request.SourceBoundaryId,
                        out var connection))
                {
                    return new WorldGraphHandoffResult(WorldGraphRuntimeSessionStatus.HandoffConnectionMissing);
                }

                if (!connection.IsValid)
                {
                    return new WorldGraphHandoffResult(WorldGraphRuntimeSessionStatus.HandoffConnectionMissing);
                }

                if (!connection.IsWalkable)
                {
                    return new WorldGraphHandoffResult(
                        WorldGraphRuntimeSessionStatus.HandoffConnectionNotWalkable,
                        connection);
                }

                var targetLoad = await _host.LoadTargetAsync(connection, cancellationToken);
                if (!targetLoad.Succeeded)
                {
                    return new WorldGraphHandoffResult(
                        WorldGraphRuntimeSessionStatus.HandoffTargetLoadFailed,
                        connection,
                        targetLoad);
                }

                var switchResult = await _host.SwitchActiveGraphAsync(connection, cancellationToken);
                if (!switchResult.Succeeded)
                {
                    return new WorldGraphHandoffResult(
                        WorldGraphRuntimeSessionStatus.HandoffSwitchFailed,
                        connection,
                        targetLoad,
                        switchResult);
                }

                var unloadSource = await _host.UnloadSourceAsync(connection, cancellationToken);
                if (!unloadSource.Succeeded)
                {
                    return new WorldGraphHandoffResult(
                        WorldGraphRuntimeSessionStatus.UnloadFailed,
                        connection,
                        targetLoad,
                        unloadSource);
                }

                return new WorldGraphHandoffResult(
                    WorldGraphRuntimeSessionStatus.HandoffCompleted,
                    connection,
                    targetLoad,
                    unloadSource);
            }
            catch (OperationCanceledException)
            {
                return new WorldGraphHandoffResult(WorldGraphRuntimeSessionStatus.Cancelled);
            }
            catch (Exception ex)
            {
                return new WorldGraphHandoffResult(
                    WorldGraphRuntimeSessionStatus.Failed,
                    exception: ex);
            }
            finally
            {
                _operationInProgress = false;
            }
        }
    }
}
