using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph.Traversal
{
    public enum WorldTraversalBoundaryKind
    {
        Solid = 0,
        Streaming = 1,
        Portal = 2,
        Recovery = 3
    }

    public enum WorldTraversalAnchorKind
    {
        Spawn = 0,
        Recovery = 1
    }

    [Serializable]
    public sealed class WorldTraversalBoundaryDefinition
    {
        [SerializeField] private string _boundaryId;
        [SerializeField] private WorldTraversalBoundaryKind _kind;
        [SerializeField] private Bounds _localBounds;
        [SerializeField] private string _targetCellId;

        public WorldTraversalBoundaryDefinition(
            string boundaryId,
            WorldTraversalBoundaryKind kind,
            Bounds localBounds,
            string targetCellId = "")
        {
            _boundaryId = boundaryId ?? string.Empty;
            _kind = kind;
            _localBounds = localBounds;
            _targetCellId = targetCellId ?? string.Empty;
        }

        public string BoundaryId => _boundaryId;
        public WorldTraversalBoundaryKind Kind => _kind;
        public Bounds LocalBounds => _localBounds;
        public string TargetCellId => _targetCellId;
    }

    [Serializable]
    public sealed class WorldTraversalAnchorDefinition
    {
        [SerializeField] private string _anchorId;
        [SerializeField] private WorldTraversalAnchorKind _kind;
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private Vector3 _forward;

        public WorldTraversalAnchorDefinition(
            string anchorId,
            WorldTraversalAnchorKind kind,
            Vector3 localPosition,
            Vector3 forward)
        {
            _anchorId = anchorId ?? string.Empty;
            _kind = kind;
            _localPosition = localPosition;
            _forward = forward.sqrMagnitude > Mathf.Epsilon ? forward.normalized : Vector3.forward;
        }

        public string AnchorId => _anchorId;
        public WorldTraversalAnchorKind Kind => _kind;
        public Vector3 LocalPosition => _localPosition;
        public Vector3 Forward => _forward;
    }

    [Serializable]
    public sealed class WorldCellTraversalDefinition
    {
        [SerializeField] private string _cellId;
        [SerializeField] private Bounds _localBounds;
        [SerializeField] private float _recoveryHeight;
        [SerializeField] private List<WorldTraversalBoundaryDefinition> _boundaries = new();
        [SerializeField] private List<WorldTraversalAnchorDefinition> _anchors = new();

        public WorldCellTraversalDefinition(
            string cellId,
            Bounds localBounds,
            float recoveryHeight,
            IEnumerable<WorldTraversalBoundaryDefinition> boundaries,
            IEnumerable<WorldTraversalAnchorDefinition> anchors)
        {
            _cellId = cellId ?? string.Empty;
            _localBounds = localBounds;
            _recoveryHeight = recoveryHeight;
            SetBoundaries(boundaries);
            SetAnchors(anchors);
        }

        public string CellId => _cellId;
        public Bounds LocalBounds => _localBounds;
        public float RecoveryHeight => _recoveryHeight;
        public IReadOnlyList<WorldTraversalBoundaryDefinition> Boundaries => _boundaries;
        public IReadOnlyList<WorldTraversalAnchorDefinition> Anchors => _anchors;

        public void SetBoundaries(IEnumerable<WorldTraversalBoundaryDefinition> boundaries)
        {
            _boundaries = boundaries == null
                ? new List<WorldTraversalBoundaryDefinition>()
                : new List<WorldTraversalBoundaryDefinition>(boundaries);
        }

        public void SetAnchors(IEnumerable<WorldTraversalAnchorDefinition> anchors)
        {
            _anchors = anchors == null
                ? new List<WorldTraversalAnchorDefinition>()
                : new List<WorldTraversalAnchorDefinition>(anchors);
        }
    }
}
