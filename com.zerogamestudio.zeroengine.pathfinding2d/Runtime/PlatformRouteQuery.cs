using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformRouteQuery
    {
        public readonly Vector3 StartPosition;
        public readonly Vector3 TargetPosition;
        public readonly bool ProjectTargetToGround;
        public readonly bool AllowPartialPath;

        public PlatformRouteQuery(
            Vector3 startPosition,
            Vector3 targetPosition,
            bool projectTargetToGround = true,
            bool allowPartialPath = false)
        {
            StartPosition = startPosition;
            TargetPosition = targetPosition;
            ProjectTargetToGround = projectTargetToGround;
            AllowPartialPath = allowPartialPath;
        }
    }
}
