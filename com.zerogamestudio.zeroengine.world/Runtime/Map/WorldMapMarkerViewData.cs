using UnityEngine;

namespace ZeroEngine.World.Map
{
    public readonly struct WorldMapMarkerViewData
    {
        public WorldMapMarkerViewData(
            WorldMapMarkerDefinition marker,
            Vector2 normalizedPosition,
            bool isInViewport,
            bool isSelected)
        {
            Marker = marker;
            NormalizedPosition = normalizedPosition;
            IsInViewport = isInViewport;
            IsSelected = isSelected;
        }

        public WorldMapMarkerDefinition Marker { get; }
        public Vector2 NormalizedPosition { get; }
        public bool IsInViewport { get; }
        public bool IsSelected { get; }
    }
}
