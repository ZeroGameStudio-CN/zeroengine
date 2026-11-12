using System;
using UnityEngine;

namespace ZeroEngine.World.WorldGraph
{
    [Serializable]
    public readonly struct WorldGraphRuntimeLocation
    {
        public WorldGraphRuntimeLocation(
            string worldGraphId,
            string regionId,
            string cellId,
            string anchorId,
            string locationName,
            Vector3 cellWorldOrigin,
            Vector3 cellLocalPosition,
            Quaternion cellLocalRotation,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            WorldGraphId = worldGraphId ?? string.Empty;
            RegionId = regionId ?? string.Empty;
            CellId = cellId ?? string.Empty;
            AnchorId = anchorId ?? string.Empty;
            LocationName = locationName ?? string.Empty;
            CellWorldOrigin = cellWorldOrigin;
            CellLocalPosition = cellLocalPosition;
            CellLocalRotation = cellLocalRotation;
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
        }

        public string WorldGraphId { get; }
        public string RegionId { get; }
        public string CellId { get; }
        public string AnchorId { get; }
        public string LocationName { get; }
        public Vector3 CellWorldOrigin { get; }
        public Vector3 CellLocalPosition { get; }
        public Quaternion CellLocalRotation { get; }
        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
        public bool HasAnchor => !string.IsNullOrWhiteSpace(AnchorId);
        public bool IsValid => !string.IsNullOrWhiteSpace(WorldGraphId)
                               && !string.IsNullOrWhiteSpace(CellId);
    }
}
