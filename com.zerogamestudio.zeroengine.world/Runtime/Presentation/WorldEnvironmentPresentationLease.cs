using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldEnvironmentPresentationLease : IDisposable
    {
        private WorldEnvironmentPresentationCoordinator _coordinator;

        internal WorldEnvironmentPresentationLease(
            WorldEnvironmentPresentationCoordinator coordinator,
            long tokenId,
            WorldEnvironmentPresentationRequest request)
        {
            _coordinator = coordinator;
            Snapshot = new WorldEnvironmentPresentationSnapshot(tokenId, request);
        }

        public WorldEnvironmentPresentationSnapshot Snapshot { get; }
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
