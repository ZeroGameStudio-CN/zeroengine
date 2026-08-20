using System;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    [Serializable]
    public readonly struct WorldPosition
    {
        public WorldPosition(
            string worldGraphId,
            string regionId,
            string cellId,
            string anchorId,
            Vector3 cellLocalPosition,
            Quaternion cellLocalRotation)
            : this(
                worldGraphId,
                regionId,
                cellId,
                anchorId,
                cellLocalPosition,
                cellLocalRotation,
                cellLocalPosition,
                cellLocalRotation)
        {
        }

        public WorldPosition(
            string worldGraphId,
            string regionId,
            string cellId,
            string anchorId,
            Vector3 cellLocalPosition,
            Quaternion cellLocalRotation,
            Vector3 worldSpacePosition,
            Quaternion worldSpaceRotation)
        {
            WorldGraphId = worldGraphId;
            RegionId = regionId;
            CellId = cellId;
            AnchorId = anchorId;
            CellLocalPosition = cellLocalPosition;
            CellLocalRotation = cellLocalRotation;
            WorldSpacePosition = worldSpacePosition;
            WorldSpaceRotation = worldSpaceRotation;
        }

        public string WorldGraphId { get; }
        public string RegionId { get; }
        public string CellId { get; }
        public string AnchorId { get; }
        public Vector3 CellLocalPosition { get; }
        public Quaternion CellLocalRotation { get; }
        public Vector3 WorldSpacePosition { get; }
        public Quaternion WorldSpaceRotation { get; }
        public bool HasAnchor => !string.IsNullOrWhiteSpace(AnchorId);
    }
}
