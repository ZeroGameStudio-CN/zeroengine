using System;
using System.Collections.Generic;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldVisibilityCoordinator
    {
        private readonly Dictionary<long, WorldVisibilityRequest> _requests = new();
        private readonly Dictionary<string, float> _effectiveByTarget = new(StringComparer.Ordinal);
        private long _nextTokenId;
        private bool _isDispatchingChange;

        public event Action<WorldVisibilitySnapshot, WorldVisibilitySnapshot> EffectiveVisibilityChanged;

        public int ActiveRequestCount => _requests.Count;

        public WorldVisibilityLease Acquire(WorldVisibilityRequest request)
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
                RecalculateTarget(request.TargetId, true);
            }
            catch
            {
                _requests.Remove(tokenId);
                RecalculateTarget(request.TargetId, false);
                throw;
            }

            return new WorldVisibilityLease(this, tokenId, request);
        }

        public WorldVisibilitySnapshot GetEffectiveSnapshot(string targetId)
        {
            var normalizedTargetId = NormalizeTargetId(targetId);
            return new WorldVisibilitySnapshot(
                normalizedTargetId,
                _effectiveByTarget.TryGetValue(normalizedTargetId, out var visibility)
                    ? visibility
                    : 1f);
        }

        public void CopyActiveSnapshots(List<WorldVisibilityRequestSnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var pair in _requests)
            {
                results.Add(new WorldVisibilityRequestSnapshot(pair.Key, pair.Value));
            }

            results.Sort(static (left, right) =>
            {
                var targetComparison = string.CompareOrdinal(left.TargetId, right.TargetId);
                if (targetComparison != 0)
                {
                    return targetComparison;
                }

                var channelComparison = string.CompareOrdinal(left.ChannelId, right.ChannelId);
                return channelComparison != 0
                    ? channelComparison
                    : left.TokenId.CompareTo(right.TokenId);
            });
        }

        internal void Release(long tokenId)
        {
            ThrowIfDispatchingChange();
            if (!_requests.TryGetValue(tokenId, out var request)
                || !_requests.Remove(tokenId))
            {
                return;
            }

            RecalculateTarget(request.TargetId, true);
        }

        private void RecalculateTarget(string targetId, bool notifyChange)
        {
            var minimumByChannel = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var request in _requests.Values)
            {
                if (!string.Equals(request.TargetId, targetId, StringComparison.Ordinal)
                    || (minimumByChannel.TryGetValue(request.ChannelId, out var currentMinimum)
                        && currentMinimum <= request.Visibility))
                {
                    continue;
                }

                minimumByChannel[request.ChannelId] = request.Visibility;
            }

            var channelIds = new List<string>(minimumByChannel.Keys);
            channelIds.Sort(StringComparer.Ordinal);
            var product = 1d;
            foreach (var channelId in channelIds)
            {
                product *= minimumByChannel[channelId];
            }

            var nextVisibility = (float)Math.Max(0d, Math.Min(1d, product));
            var previousVisibility = _effectiveByTarget.TryGetValue(targetId, out var current)
                ? current
                : 1f;
            if (previousVisibility.Equals(nextVisibility))
            {
                return;
            }

            if (nextVisibility.Equals(1f))
            {
                _effectiveByTarget.Remove(targetId);
            }
            else
            {
                _effectiveByTarget[targetId] = nextVisibility;
            }

            if (notifyChange)
            {
                DispatchChange(
                    new WorldVisibilitySnapshot(targetId, previousVisibility),
                    new WorldVisibilitySnapshot(targetId, nextVisibility));
            }
        }

        private void DispatchChange(
            WorldVisibilitySnapshot previousSnapshot,
            WorldVisibilitySnapshot nextSnapshot)
        {
            _isDispatchingChange = true;
            try
            {
                EffectiveVisibilityChanged?.Invoke(previousSnapshot, nextSnapshot);
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
                    "Visibility requests cannot be mutated during change dispatch.");
            }
        }

        private static string NormalizeTargetId(string targetId)
        {
            var normalized = targetId?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("A non-empty target identifier is required.", nameof(targetId));
            }

            return normalized;
        }
    }
}
