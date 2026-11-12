using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.World.Map
{
    public sealed class WorldMapDiscoveryState
    {
        private readonly HashSet<string> _discoveredCellIds = new HashSet<string>();
        private readonly HashSet<string> _visitedAnchorIds = new HashSet<string>();
        private readonly HashSet<string> _unlockedFastTravelNodeIds = new HashSet<string>();

        public int DiscoveredCellCount => _discoveredCellIds.Count;
        public int VisitedAnchorCount => _visitedAnchorIds.Count;
        public int UnlockedFastTravelNodeCount => _unlockedFastTravelNodeIds.Count;

        public bool DiscoverCell(string cellId)
        {
            return AddStableId(_discoveredCellIds, cellId);
        }

        public bool VisitAnchor(string anchorId)
        {
            return AddStableId(_visitedAnchorIds, anchorId);
        }

        public bool UnlockFastTravelNode(string nodeId)
        {
            return AddStableId(_unlockedFastTravelNodeIds, nodeId);
        }

        public bool IsCellDiscovered(string cellId)
        {
            return !string.IsNullOrWhiteSpace(cellId) && _discoveredCellIds.Contains(cellId);
        }

        public bool IsAnchorVisited(string anchorId)
        {
            return !string.IsNullOrWhiteSpace(anchorId) && _visitedAnchorIds.Contains(anchorId);
        }

        public bool IsFastTravelNodeUnlocked(string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) && _unlockedFastTravelNodeIds.Contains(nodeId);
        }

        public WorldMapDiscoverySnapshot CaptureSnapshot()
        {
            return new WorldMapDiscoverySnapshot(
                _discoveredCellIds.OrderBy(id => id).ToArray(),
                _visitedAnchorIds.OrderBy(id => id).ToArray(),
                _unlockedFastTravelNodeIds.OrderBy(id => id).ToArray());
        }

        public void RestoreSnapshot(WorldMapDiscoverySnapshot snapshot)
        {
            _discoveredCellIds.Clear();
            _visitedAnchorIds.Clear();
            _unlockedFastTravelNodeIds.Clear();
            AddRange(_discoveredCellIds, snapshot.DiscoveredCellIds);
            AddRange(_visitedAnchorIds, snapshot.VisitedAnchorIds);
            AddRange(_unlockedFastTravelNodeIds, snapshot.UnlockedFastTravelNodeIds);
        }

        public void Clear()
        {
            _discoveredCellIds.Clear();
            _visitedAnchorIds.Clear();
            _unlockedFastTravelNodeIds.Clear();
        }

        private static bool AddStableId(HashSet<string> target, string id)
        {
            return WorldMapStableId.IsStableId(id) && target.Add(id);
        }

        private static void AddRange(HashSet<string> target, IReadOnlyList<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                AddStableId(target, ids[i]);
            }
        }
    }

    public readonly struct WorldMapDiscoverySnapshot
    {
        public WorldMapDiscoverySnapshot(
            IReadOnlyList<string> discoveredCellIds,
            IReadOnlyList<string> visitedAnchorIds,
            IReadOnlyList<string> unlockedFastTravelNodeIds)
        {
            DiscoveredCellIds = Copy(discoveredCellIds);
            VisitedAnchorIds = Copy(visitedAnchorIds);
            UnlockedFastTravelNodeIds = Copy(unlockedFastTravelNodeIds);
        }

        public IReadOnlyList<string> DiscoveredCellIds { get; }
        public IReadOnlyList<string> VisitedAnchorIds { get; }
        public IReadOnlyList<string> UnlockedFastTravelNodeIds { get; }

        private static string[] Copy(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return new string[0];
            }

            var result = new string[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                result[i] = source[i] ?? string.Empty;
            }

            return result;
        }
    }
}
