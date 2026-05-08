using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformPathRequest
    {
        public readonly Vector3 Start;
        public readonly Vector3 Target;
        public readonly bool ForceRequest;
        public readonly bool ProjectTargetToGround;
        public readonly float ProjectionDistance;

        public PlatformPathRequest(
            Vector3 start,
            Vector3 target,
            bool forceRequest = false,
            bool projectTargetToGround = false,
            float projectionDistance = 1.5f)
        {
            Start = start;
            Target = target;
            ForceRequest = forceRequest;
            ProjectTargetToGround = projectTargetToGround;
            ProjectionDistance = projectionDistance;
        }
    }
}
