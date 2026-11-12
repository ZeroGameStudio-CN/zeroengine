using System;
using System.Collections.Generic;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldCameraPresentationCoordinator
    {
        private readonly Dictionary<long, WorldCameraPresentationRequest> _requests = new();
        private long _nextTokenId;
        private bool _isDispatchingChange;

        public event Action<WorldCameraPresentationSnapshot, WorldCameraPresentationSnapshot>
            EffectiveSnapshotChanged;

        public WorldCameraPresentationSnapshot EffectiveSnapshot { get; private set; }
        public int ActiveRequestCount => _requests.Count;

        public bool ContainsToken(long tokenId)
        {
            return tokenId > 0 && _requests.ContainsKey(tokenId);
        }

        public WorldCameraPresentationLease Acquire(WorldCameraPresentationRequest request)
        {
            ThrowIfDispatchingChange();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var tokenId = ++_nextTokenId;
            _requests.Add(tokenId, request);
            try
            {
                RecalculateEffectiveSnapshot(true);
            }
            catch
            {
                _requests.Remove(tokenId);
                RecalculateEffectiveSnapshot(false);
                throw;
            }

            return new WorldCameraPresentationLease(this, tokenId, request);
        }

        public void CopyActiveSnapshots(List<WorldCameraPresentationSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var pair in _requests)
            {
                results.Add(new WorldCameraPresentationSnapshot(pair.Key, pair.Value));
            }

            results.Sort(static (left, right) => left.TokenId.CompareTo(right.TokenId));
        }

        internal void Release(long tokenId)
        {
            ThrowIfDispatchingChange();
            if (!_requests.Remove(tokenId))
            {
                return;
            }

            RecalculateEffectiveSnapshot(true);
        }

        private void RecalculateEffectiveSnapshot(bool notifyChange)
        {
            var winningTokenId = 0L;
            WorldCameraPresentationRequest winningRequest = null;
            foreach (var pair in _requests)
            {
                if (winningRequest != null
                    && (pair.Value.Priority < winningRequest.Priority
                        || (pair.Value.Priority == winningRequest.Priority
                            && pair.Key > winningTokenId)))
                {
                    continue;
                }

                winningTokenId = pair.Key;
                winningRequest = pair.Value;
            }

            var nextSnapshot = winningRequest == null
                ? default
                : new WorldCameraPresentationSnapshot(winningTokenId, winningRequest);
            if (EffectiveSnapshot == nextSnapshot)
            {
                return;
            }

            var previousSnapshot = EffectiveSnapshot;
            EffectiveSnapshot = nextSnapshot;
            if (notifyChange)
            {
                DispatchChange(previousSnapshot, nextSnapshot);
            }
        }

        private void DispatchChange(
            WorldCameraPresentationSnapshot previousSnapshot,
            WorldCameraPresentationSnapshot nextSnapshot)
        {
            _isDispatchingChange = true;
            try
            {
                EffectiveSnapshotChanged?.Invoke(previousSnapshot, nextSnapshot);
            }
            finally
            {
                _isDispatchingChange = false;
            }
        }

        private void ThrowIfDispatchingChange()
        {
            if (_isDispatchingChange)
            {
                throw new InvalidOperationException(
                    "Camera presentation requests cannot be mutated during change dispatch.");
            }
        }
    }
}
