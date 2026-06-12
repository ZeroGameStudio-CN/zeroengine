using UnityEngine;

namespace ZeroEngine.World.Map
{
    public readonly struct WorldMapCoordinateMapper
    {
        public WorldMapCoordinateMapper(Bounds worldBounds)
        {
            WorldBounds = worldBounds;
        }

        public Bounds WorldBounds { get; }

        public bool TryWorldToNormalized(Vector3 worldPosition, out Vector2 normalized)
        {
            normalized = default;
            var size = WorldBounds.size;
            if (size.x <= 0f || size.z <= 0f)
            {
                return false;
            }

            var min = WorldBounds.min;
            normalized = new Vector2(
                (worldPosition.x - min.x) / size.x,
                (worldPosition.z - min.z) / size.z);
            return normalized.x >= 0f && normalized.x <= 1f && normalized.y >= 0f && normalized.y <= 1f;
        }

        public Vector2 WorldToNormalizedClamped(Vector3 worldPosition)
        {
            if (!TryWorldToNormalized(worldPosition, out var normalized))
            {
                normalized.x = Mathf.Clamp01(normalized.x);
                normalized.y = Mathf.Clamp01(normalized.y);
            }

            return normalized;
        }
    }
}
