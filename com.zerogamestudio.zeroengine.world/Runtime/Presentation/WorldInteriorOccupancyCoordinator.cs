using System;
using System.Collections.Generic;

namespace ZeroEngine.World.Presentation
{
    public sealed class WorldInteriorOccupancyCoordinator
    {
        private readonly Dictionary<long, WorldInteriorOccupancyRequest> _requests = new();
        private readonly Dictionary<string, WorldInteriorOccupancySnapshot> _effectiveBySubject =
            new(StringComparer.Ordinal);
        private long _nextTokenId;
        private bool _isDispatchingChange;

        public event Action<WorldInteriorOccupancySnapshot, WorldInteriorOccupancySnapshot>
            EffectiveSnapshotChanged;

        public int ActiveRequestCount => _requests.Count;

        public WorldInteriorOccupancyLease Acquire(WorldInteriorOccupancyRequest request)
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
                RecalculateSubject(request.SubjectId, true);
            }
            catch
            {
                _requests.Remove(tokenId);
                RecalculateSubject(request.SubjectId, false);
                throw;
            }

            return new WorldInteriorOccupancyLease(this, tokenId, request);
        }

        public WorldInteriorOccupancySnapshot GetEffectiveSnapshot(string subjectId)
        {
            var normalizedSubjectId = NormalizeSubjectId(subjectId);
            return _effectiveBySubject.TryGetValue(normalizedSubjectId, out var snapshot)
                ? snapshot
                : WorldInteriorOccupancySnapshot.Inactive(normalizedSubjectId);
        }

        public void CopyActiveSnapshots(List<WorldInteriorOccupancySnapshot> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var pair in _requests)
            {
                results.Add(new WorldInteriorOccupancySnapshot(pair.Key, pair.Value));
            }

            results.Sort(static (left, right) =>
            {
                var subjectComparison = string.CompareOrdinal(left.SubjectId, right.SubjectId);
                return subjectComparison != 0
                    ? subjectComparison
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

            RecalculateSubject(request.SubjectId, true);
        }

        private void RecalculateSubject(string subjectId, bool notifyChange)
        {
            var winningTokenId = 0L;
            WorldInteriorOccupancyRequest winningRequest = null;
            foreach (var pair in _requests)
            {
                if (!string.Equals(pair.Value.SubjectId, subjectId, StringComparison.Ordinal)
                    || (winningRequest != null
                        && (pair.Value.Priority < winningRequest.Priority
                            || (pair.Value.Priority == winningRequest.Priority
                                && pair.Key < winningTokenId))))
                {
                    continue;
                }

                winningTokenId = pair.Key;
                winningRequest = pair.Value;
            }

            var previousSnapshot = _effectiveBySubject.TryGetValue(subjectId, out var current)
                ? current
                : WorldInteriorOccupancySnapshot.Inactive(subjectId);
            var nextSnapshot = winningRequest == null
                ? WorldInteriorOccupancySnapshot.Inactive(subjectId)
                : new WorldInteriorOccupancySnapshot(winningTokenId, winningRequest);
            if (previousSnapshot == nextSnapshot)
            {
                return;
            }

            if (nextSnapshot.IsOccupied)
            {
                _effectiveBySubject[subjectId] = nextSnapshot;
            }
            else
            {
                _effectiveBySubject.Remove(subjectId);
            }

            if (notifyChange)
            {
                DispatchChange(previousSnapshot, nextSnapshot);
            }
        }

        private void DispatchChange(
            WorldInteriorOccupancySnapshot previousSnapshot,
            WorldInteriorOccupancySnapshot nextSnapshot)
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
                    "Interior occupancy requests cannot be mutated during change dispatch.");
            }
        }

        private static string NormalizeSubjectId(string subjectId)
        {
            var normalized = subjectId?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                throw new ArgumentException("A non-empty subject identifier is required.", nameof(subjectId));
            }

            return normalized;
        }
    }
}
