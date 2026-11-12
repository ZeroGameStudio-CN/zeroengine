using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldInteriorOccupancyLease : IDisposable
    {
        private WorldInteriorOccupancyCoordinator _coordinator;

        internal WorldInteriorOccupancyLease(
            WorldInteriorOccupancyCoordinator coordinator,
            long tokenId,
            WorldInteriorOccupancyRequest request)
        {
            _coordinator = coordinator;
            Snapshot = new WorldInteriorOccupancySnapshot(tokenId, request);
        }

        public WorldInteriorOccupancySnapshot Snapshot { get; }
        public long TokenId => Snapshot.TokenId;
        public bool IsReleased => _coordinator == null;

        public void Dispose()
        {
            var coordinator = _coordinator;
            if (coordinator == null)
            {
                return;
            }

            coordinator.Release(TokenId);
            _coordinator = null;
        }
    }
}
