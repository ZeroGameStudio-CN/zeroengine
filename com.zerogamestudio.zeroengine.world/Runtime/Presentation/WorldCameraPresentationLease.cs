using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldCameraPresentationLease : IDisposable
    {
        private WorldCameraPresentationCoordinator _coordinator;

        internal WorldCameraPresentationLease(
            WorldCameraPresentationCoordinator coordinator,
            long tokenId,
            WorldCameraPresentationRequest request)
        {
            _coordinator = coordinator;
            Snapshot = new WorldCameraPresentationSnapshot(tokenId, request);
        }

        public WorldCameraPresentationSnapshot Snapshot { get; }
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
