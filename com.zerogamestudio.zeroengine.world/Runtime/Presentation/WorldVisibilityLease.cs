using System;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldVisibilityLease : IDisposable
    {
        private WorldVisibilityCoordinator _coordinator;

        internal WorldVisibilityLease(
            WorldVisibilityCoordinator coordinator,
            long tokenId,
            WorldVisibilityRequest request)
        {
            _coordinator = coordinator;
            Snapshot = new WorldVisibilityRequestSnapshot(tokenId, request);
        }

        public WorldVisibilityRequestSnapshot Snapshot { get; }
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
