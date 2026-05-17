using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// A continuous standable surface. Composite and tilemap colliders can contain
    /// many independent surface segments, so collider identity alone is not enough
    /// for platform navigation decisions.
    /// </summary>
    [System.Serializable]
    public class PlatformSurfaceSegment
    {
        public int GroupId;
        public Collider2D Collider;
        public float Left;
        public float Right;
        public float Y;
        public bool IsOneWay;
        public int LeftNodeId = -1;
        public int RightNodeId = -1;
        public readonly List<int> NodeIds = new List<int>();

        public int Id => GroupId;
        public float MinX => Left;
        public float MaxX => Right;
        public float Width => Right - Left;
        public Vector2 Center => new Vector2((Left + Right) * 0.5f, Y);

        public bool ContainsX(float x, float tolerance = 0f)
        {
            return x >= Left - tolerance && x <= Right + tolerance;
        }

        public float DistanceTo(Vector2 position)
        {
            float clampedX = Mathf.Clamp(position.x, Left, Right);
            return Vector2.Distance(position, new Vector2(clampedX, Y));
        }
    }
}
