using System;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    [Serializable]
    public sealed class WorldGraphConnectionDefinition
    {
        [SerializeField] private string _connectionId;
        [SerializeField] private string _sourceWorldGraphId;
        [SerializeField] private string _sourceCellId;
        [SerializeField] private string _sourceBoundaryId;
        [SerializeField] private string _sourceAnchorId;
        [SerializeField] private string _targetWorldGraphId;
        [SerializeField] private string _targetWorldGraphAddress;
        [SerializeField] private string _targetCellId;
        [SerializeField] private string _targetAnchorId;
        [SerializeField] private WorldTravelMode _travelMode;

        public WorldGraphConnectionDefinition(
            string connectionId,
            string sourceWorldGraphId,
            string sourceCellId,
            string sourceBoundaryId,
            string sourceAnchorId,
            string targetWorldGraphId,
            string targetWorldGraphAddress,
            string targetCellId,
            string targetAnchorId,
            WorldTravelMode travelMode)
        {
            _connectionId = connectionId ?? string.Empty;
            _sourceWorldGraphId = sourceWorldGraphId ?? string.Empty;
            _sourceCellId = sourceCellId ?? string.Empty;
            _sourceBoundaryId = sourceBoundaryId ?? string.Empty;
            _sourceAnchorId = sourceAnchorId ?? string.Empty;
            _targetWorldGraphId = targetWorldGraphId ?? string.Empty;
            _targetWorldGraphAddress = targetWorldGraphAddress ?? string.Empty;
            _targetCellId = targetCellId ?? string.Empty;
            _targetAnchorId = targetAnchorId ?? string.Empty;
            _travelMode = travelMode;
        }

        public string ConnectionId => _connectionId;
        public string SourceWorldGraphId => _sourceWorldGraphId;
        public string SourceCellId => _sourceCellId;
        public string SourceBoundaryId => _sourceBoundaryId;
        public string SourceAnchorId => _sourceAnchorId;
        public string TargetWorldGraphId => _targetWorldGraphId;
        public string TargetWorldGraphAddress => _targetWorldGraphAddress;
        public string TargetCellId => _targetCellId;
        public string TargetAnchorId => _targetAnchorId;
        public WorldTravelMode TravelMode => _travelMode;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(ConnectionId)
            && !string.IsNullOrWhiteSpace(SourceWorldGraphId)
            && !string.IsNullOrWhiteSpace(SourceCellId)
            && !string.IsNullOrWhiteSpace(SourceBoundaryId)
            && !string.IsNullOrWhiteSpace(SourceAnchorId)
            && !string.IsNullOrWhiteSpace(TargetWorldGraphId)
            && !string.IsNullOrWhiteSpace(TargetWorldGraphAddress)
            && !string.IsNullOrWhiteSpace(TargetCellId)
            && !string.IsNullOrWhiteSpace(TargetAnchorId);

        public bool IsWalkable =>
            TravelMode == WorldTravelMode.SeamlessWalk
            || TravelMode == WorldTravelMode.SeamlessInterior;
    }
}
