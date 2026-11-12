using System;
using System.Collections.Generic;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldEnvironmentPresentationCoordinator
    {
        private readonly Dictionary<long, WorldEnvironmentPresentationRequest> _requests = new();
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private WorldEnvironmentPresentationSnapshot _effectiveSnapshot;
        private long _nextTokenId;
        private bool _isDispatchingChange;

        public event Action<WorldEnvironmentPresentationSnapshot, WorldEnvironmentPresentationSnapshot>
            EffectiveSnapshotChanged;

        public WorldEnvironmentPresentationSnapshot EffectiveSnapshot
        {
            get
            {
                ThrowIfNotOwnerThread();
                return _effectiveSnapshot;
            }
        }

        public int ActiveRequestCount
        {
            get
            {
                ThrowIfNotOwnerThread();
                return _requests.Count;
            }
        }

        public WorldEnvironmentPresentationLease Acquire(
            WorldEnvironmentPresentationRequest request)
        {
            ThrowIfNotOwnerThread();
            ThrowIfDispatchingChange();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var tokenId = NextTokenId();
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

            return new WorldEnvironmentPresentationLease(this, tokenId, request);
        }

        public void CopyActiveSnapshots(List<WorldEnvironmentPresentationSnapshot> results)
        {
            ThrowIfNotOwnerThread();
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var pair in _requests)
            {
                results.Add(new WorldEnvironmentPresentationSnapshot(pair.Key, pair.Value));
            }

            results.Sort(static (left, right) => left.TokenId.CompareTo(right.TokenId));
        }

        internal void Release(long tokenId)
        {
            ThrowIfNotOwnerThread();
            ThrowIfDispatchingChange();
            if (!_requests.TryGetValue(tokenId, out var request)
                || !_requests.Remove(tokenId))
            {
                return;
            }

            try
            {
                RecalculateEffectiveSnapshot(true);
            }
            catch
            {
                _requests.Add(tokenId, request);
                RecalculateEffectiveSnapshot(false);
                throw;
            }
        }

        private long NextTokenId()
        {
            if (_nextTokenId == long.MaxValue)
            {
                throw new InvalidOperationException(
                    "Environment presentation request tokens have been exhausted.");
            }

            return ++_nextTokenId;
        }

        private void RecalculateEffectiveSnapshot(bool notifyChange)
        {
            var winningTokenId = 0L;
            WorldEnvironmentPresentationRequest winningRequest = null;
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
                : new WorldEnvironmentPresentationSnapshot(winningTokenId, winningRequest);
            if (_effectiveSnapshot == nextSnapshot)
            {
                return;
            }

            var previousSnapshot = _effectiveSnapshot;
            _effectiveSnapshot = nextSnapshot;
            if (notifyChange)
            {
                DispatchChange(previousSnapshot, nextSnapshot);
            }
        }

        private void DispatchChange(
            WorldEnvironmentPresentationSnapshot previousSnapshot,
            WorldEnvironmentPresentationSnapshot nextSnapshot)
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

        private void ThrowIfNotOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Environment presentation coordination must stay on its owning thread.");
            }
        }

        private void ThrowIfDispatchingChange()
        {
            if (_isDispatchingChange)
            {
                throw new InvalidOperationException(
                    "Environment presentation requests cannot be mutated during change dispatch.");
            }
        }
    }
}
