using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public static class PlatformTargetProjection
    {
        public static Vector2 ProjectTargetToGround(
            Vector2 targetPosition,
            LayerMask platformMask,
            float checkDistance = 1.5f)
        {
            var hit = Physics2D.Raycast(
                targetPosition + Vector2.up * 0.5f,
                Vector2.down,
                checkDistance,
                platformMask);

            return hit.collider ? hit.point : targetPosition;
        }
    }
}
