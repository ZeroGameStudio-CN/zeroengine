using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.World.Map
{
    public sealed class WorldMapViewModel
    {
        private readonly WorldMapState _state;
        private readonly List<WorldMapMarkerDefinition> _markers = new List<WorldMapMarkerDefinition>();
        private readonly List<WorldMapMarkerViewData> _viewMarkers = new List<WorldMapMarkerViewData>();

        public WorldMapViewModel(WorldMapState state)
        {
            _state = state;
        }

        public bool TryBuildSnapshot(
            WorldMapViewportState viewport,
            out WorldMapViewSnapshot snapshot,
            out string error,
            WorldMapMarkerFilter filter = default,
            float aspectRatio = 1f,
            bool includeOutOfBounds = false)
        {
            snapshot = default;
            if (_state == null)
            {
                error = "World map state is null.";
                return false;
            }

            if (viewport == null)
            {
                error = "World map viewport is null.";
                return false;
            }

            if (!_state.MarkerRegistry.TryCollectMarkers(_markers, out error, filter, _state.Discovery))
            {
                _viewMarkers.Clear();
                return false;
            }

            _viewMarkers.Clear();
            for (var i = 0; i < _markers.Count; i++)
            {
                var marker = _markers[i];
                var isInViewport = viewport.TryWorldToNormalized(marker.WorldPosition, out var normalized, aspectRatio);
                if (!isInViewport)
                {
                    if (!includeOutOfBounds)
                    {
                        continue;
                    }

                    normalized = viewport.WorldToNormalizedClamped(marker.WorldPosition, aspectRatio);
                }

                _viewMarkers.Add(new WorldMapMarkerViewData(
                    marker,
                    normalized,
                    isInViewport,
                    marker.MarkerId == viewport.SelectedMarkerId));
            }

            snapshot = new WorldMapViewSnapshot(
                viewport.CenterWorldPosition,
                viewport.Zoom,
                viewport.RotationDegrees,
                viewport.SelectedMarkerId,
                _viewMarkers);
            error = string.Empty;
            return true;
        }
    }
}
