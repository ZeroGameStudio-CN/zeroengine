using UnityEngine;

namespace ZeroEngine.World.Map
{
    public sealed class WorldMapViewportState
    {
        public WorldMapViewportState(
            Vector3 centerWorldPosition,
            float zoom = 30f,
            float minZoom = 10f,
            float maxZoom = 100f)
        {
            CenterWorldPosition = centerWorldPosition;
            MinZoom = Mathf.Max(0.01f, minZoom);
            MaxZoom = Mathf.Max(MinZoom, maxZoom);
            SetZoom(zoom);
        }

        public Vector3 CenterWorldPosition { get; private set; }
        public float Zoom { get; private set; }
        public float MinZoom { get; private set; }
        public float MaxZoom { get; private set; }
        public float RotationDegrees { get; private set; }
        public string SelectedMarkerId { get; private set; } = string.Empty;

        public void SetCenter(Vector3 centerWorldPosition)
        {
            CenterWorldPosition = centerWorldPosition;
        }

        public void SetZoomRange(float minZoom, float maxZoom)
        {
            MinZoom = Mathf.Max(0.01f, minZoom);
            MaxZoom = Mathf.Max(MinZoom, maxZoom);
            SetZoom(Zoom);
        }

        public void SetZoom(float zoom)
        {
            Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        public void ZoomBy(float delta)
        {
            SetZoom(Zoom + delta);
        }

        public void Pan(Vector2 normalizedDelta, float aspectRatio = 1f)
        {
            var safeAspectRatio = Mathf.Max(0.01f, aspectRatio);
            CenterWorldPosition += new Vector3(
                normalizedDelta.x * Zoom * 2f * safeAspectRatio,
                0f,
                normalizedDelta.y * Zoom * 2f);
        }

        public void SetRotationDegrees(float rotationDegrees)
        {
            RotationDegrees = NormalizeDegrees(rotationDegrees);
        }

        public void SelectMarker(string markerId)
        {
            SelectedMarkerId = WorldMapStableIdUtility.IsStableId(markerId) ? markerId : string.Empty;
        }

        public Bounds CreateWorldBounds(float aspectRatio = 1f)
        {
            var safeAspectRatio = Mathf.Max(0.01f, aspectRatio);
            return new Bounds(
                CenterWorldPosition,
                new Vector3(Zoom * 2f * safeAspectRatio, 1f, Zoom * 2f));
        }

        public bool TryWorldToNormalized(Vector3 worldPosition, out Vector2 normalized, float aspectRatio = 1f)
        {
            return new WorldMapCoordinateMapper(CreateWorldBounds(aspectRatio))
                .TryWorldToNormalized(worldPosition, out normalized);
        }

        public Vector2 WorldToNormalizedClamped(Vector3 worldPosition, float aspectRatio = 1f)
        {
            return new WorldMapCoordinateMapper(CreateWorldBounds(aspectRatio))
                .WorldToNormalizedClamped(worldPosition);
        }

        private static float NormalizeDegrees(float value)
        {
            var normalized = value % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }
    }
}
