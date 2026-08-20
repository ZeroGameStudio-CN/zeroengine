using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    [CreateAssetMenu(fileName = "WorldGraph", menuName = "ZeroEngine/World/World Graph")]
    public sealed class WorldGraphSO : ScriptableObject
    {
        [SerializeField] private string _worldGraphId;
        [SerializeField] private List<WorldRegionDefinition> _regions = new();
        [SerializeField] private List<WorldTravelLinkDefinition> _travelLinks = new();
        [SerializeField] private List<WorldFastTravelNodeDefinition> _fastTravelNodes = new();

        public string WorldGraphId => _worldGraphId;
        public IReadOnlyList<WorldRegionDefinition> Regions => _regions;
        public IReadOnlyList<WorldTravelLinkDefinition> TravelLinks => _travelLinks;
        public IReadOnlyList<WorldFastTravelNodeDefinition> FastTravelNodes => _fastTravelNodes;

        public WorldRegionDefinition FindRegion(string regionId)
        {
            if (string.IsNullOrWhiteSpace(regionId))
            {
                return null;
            }

            return _regions.FirstOrDefault(region => region != null && region.RegionId == regionId);
        }

        public WorldCellDefinition FindCell(string cellId)
        {
            if (string.IsNullOrWhiteSpace(cellId))
            {
                return null;
            }

            return _regions
                .Where(region => region != null)
                .SelectMany(region => region.Cells)
                .FirstOrDefault(cell => cell != null && cell.CellId == cellId);
        }

        public WorldAnchorDefinition FindAnchor(string anchorId)
        {
            if (string.IsNullOrWhiteSpace(anchorId))
            {
                return null;
            }

            return _regions
                .Where(region => region != null)
                .SelectMany(region => region.Cells)
                .Where(cell => cell != null)
                .SelectMany(cell => cell.Anchors)
                .FirstOrDefault(anchor => anchor != null && anchor.AnchorId == anchorId);
        }

#if UNITY_EDITOR
        public void ConfigureForTests(
            string worldGraphId,
            IEnumerable<WorldRegionDefinition> regions,
            IEnumerable<WorldTravelLinkDefinition> travelLinks,
            IEnumerable<WorldFastTravelNodeDefinition> fastTravelNodes)
        {
            _worldGraphId = worldGraphId;
            _regions = regions?.ToList() ?? new List<WorldRegionDefinition>();
            _travelLinks = travelLinks?.ToList() ?? new List<WorldTravelLinkDefinition>();
            _fastTravelNodes = fastTravelNodes?.ToList() ?? new List<WorldFastTravelNodeDefinition>();
        }
#endif
    }

    [Serializable]
    public sealed class WorldRegionDefinition
    {
        [SerializeField] private string _regionId;
        [SerializeField] private string _displayName;
        [SerializeField] private List<WorldCellDefinition> _cells = new();

        public WorldRegionDefinition(string regionId, string displayName, IEnumerable<WorldCellDefinition> cells)
        {
            _regionId = regionId;
            _displayName = displayName;
            _cells = cells?.ToList() ?? new List<WorldCellDefinition>();
        }

        public string RegionId => _regionId;
        public string DisplayName => _displayName;
        public IReadOnlyList<WorldCellDefinition> Cells => _cells;
    }

    [Serializable]
    public sealed class WorldCellDefinition
    {
        [SerializeField] private string _cellId;
        [SerializeField] private string _displayName;
        [SerializeField] private WorldCellKind _cellKind;
        [SerializeField] private string _sceneAddress;
        [SerializeField] private Vector3 _worldOrigin;
        [SerializeField] private WorldCellLayer _layers;
        [SerializeField] private int _budgetWeight = 1;
        [SerializeField] private List<WorldAnchorDefinition> _anchors = new();
        [SerializeField] private List<WorldStreamingBoundaryDefinition> _streamingBoundaries = new();

        public WorldCellDefinition(
            string cellId,
            string displayName,
            WorldCellKind cellKind,
            string sceneAddress,
            WorldCellLayer layers,
            int budgetWeight,
            IEnumerable<WorldAnchorDefinition> anchors,
            IEnumerable<WorldStreamingBoundaryDefinition> streamingBoundaries)
            : this(
                cellId,
                displayName,
                cellKind,
                sceneAddress,
                Vector3.zero,
                layers,
                budgetWeight,
                anchors,
                streamingBoundaries)
        {
        }

        public WorldCellDefinition(
            string cellId,
            string displayName,
            WorldCellKind cellKind,
            string sceneAddress,
            Vector3 worldOrigin,
            WorldCellLayer layers,
            int budgetWeight,
            IEnumerable<WorldAnchorDefinition> anchors,
            IEnumerable<WorldStreamingBoundaryDefinition> streamingBoundaries)
        {
            _cellId = cellId;
            _displayName = displayName;
            _cellKind = cellKind;
            _sceneAddress = sceneAddress;
            _worldOrigin = worldOrigin;
            _layers = layers;
            _budgetWeight = budgetWeight;
            _anchors = anchors?.ToList() ?? new List<WorldAnchorDefinition>();
            _streamingBoundaries = streamingBoundaries?.ToList() ?? new List<WorldStreamingBoundaryDefinition>();
        }

        public string CellId => _cellId;
        public string DisplayName => _displayName;
        public WorldCellKind CellKind => _cellKind;
        public string SceneAddress => _sceneAddress;
        public Vector3 WorldOrigin => _worldOrigin;
        public WorldCellLayer Layers => _layers;
        public int BudgetWeight => _budgetWeight;
        public IReadOnlyList<WorldAnchorDefinition> Anchors => _anchors;
        public IReadOnlyList<WorldStreamingBoundaryDefinition> StreamingBoundaries => _streamingBoundaries;
    }

    [Serializable]
    public sealed class WorldAnchorDefinition
    {
        [SerializeField] private string _anchorId;
        [SerializeField] private string _displayName;
        [SerializeField] private WorldAnchorKind _anchorKind;
        [SerializeField] private Vector3 _cellLocalPosition;
        [SerializeField] private Vector3 _cellLocalForward;

        public WorldAnchorDefinition(
            string anchorId,
            string displayName,
            WorldAnchorKind anchorKind,
            Vector3 cellLocalPosition,
            Vector3 cellLocalForward)
        {
            _anchorId = anchorId;
            _displayName = displayName;
            _anchorKind = anchorKind;
            _cellLocalPosition = cellLocalPosition;
            _cellLocalForward = cellLocalForward;
        }

        public string AnchorId => _anchorId;
        public string DisplayName => _displayName;
        public WorldAnchorKind AnchorKind => _anchorKind;
        public Vector3 CellLocalPosition => _cellLocalPosition;
        public Vector3 CellLocalForward => _cellLocalForward;
    }

    [Serializable]
    public sealed class WorldStreamingBoundaryDefinition
    {
        [SerializeField] private string _boundaryId;
        [SerializeField] private List<string> _targetCellIds = new();

        public WorldStreamingBoundaryDefinition(string boundaryId, IEnumerable<string> targetCellIds)
        {
            _boundaryId = boundaryId;
            _targetCellIds = targetCellIds?.ToList() ?? new List<string>();
        }

        public string BoundaryId => _boundaryId;
        public IReadOnlyList<string> TargetCellIds => _targetCellIds;
    }

    [Serializable]
    public sealed class WorldTravelLinkDefinition
    {
        [SerializeField] private string _linkId;
        [SerializeField] private string _fromAnchorId;
        [SerializeField] private string _toAnchorId;
        [SerializeField] private WorldTravelMode _travelMode;
        [SerializeField] private bool _bidirectional;

        public WorldTravelLinkDefinition(
            string linkId,
            string fromAnchorId,
            string toAnchorId,
            WorldTravelMode travelMode,
            bool bidirectional)
        {
            _linkId = linkId;
            _fromAnchorId = fromAnchorId;
            _toAnchorId = toAnchorId;
            _travelMode = travelMode;
            _bidirectional = bidirectional;
        }

        public string LinkId => _linkId;
        public string FromAnchorId => _fromAnchorId;
        public string ToAnchorId => _toAnchorId;
        public WorldTravelMode TravelMode => _travelMode;
        public bool Bidirectional => _bidirectional;
    }

    [Serializable]
    public sealed class WorldFastTravelNodeDefinition
    {
        [SerializeField] private string _nodeId;
        [SerializeField] private string _anchorId;
        [SerializeField] private string _unlockFactId;

        public WorldFastTravelNodeDefinition(string nodeId, string anchorId, string unlockFactId)
        {
            _nodeId = nodeId;
            _anchorId = anchorId;
            _unlockFactId = unlockFactId;
        }

        public string NodeId => _nodeId;
        public string AnchorId => _anchorId;
        public string UnlockFactId => _unlockFactId;
    }
}
