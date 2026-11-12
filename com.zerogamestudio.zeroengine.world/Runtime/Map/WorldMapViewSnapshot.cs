using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.World.Map
{
    public readonly struct WorldMapViewSnapshot
    {
        public WorldMapViewSnapshot(
            Vector3 centerWorldPosition,
            float zoom,
            float rotationDegrees,
            string selectedMarkerId,
            IReadOnlyList<WorldMapMarkerViewData> markers)
        {
            CenterWorldPosition = centerWorldPosition;
            Zoom = zoom;
            RotationDegrees = rotationDegrees;
            SelectedMarkerId = selectedMarkerId ?? string.Empty;
            Markers = Copy(markers);
        }

        public Vector3 CenterWorldPosition { get; }
        public float Zoom { get; }
        public float RotationDegrees { get; }
        public string SelectedMarkerId { get; }
        public IReadOnlyList<WorldMapMarkerViewData> Markers { get; }

        private static WorldMapMarkerViewData[] Copy(IReadOnlyList<WorldMapMarkerViewData> source)
        {
            if (source == null || source.Count == 0)
            {
                return new WorldMapMarkerViewData[0];
            }

            var result = new WorldMapMarkerViewData[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }
    }
}
