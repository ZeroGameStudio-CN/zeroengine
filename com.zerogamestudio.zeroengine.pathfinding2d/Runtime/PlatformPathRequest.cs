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
        public readonly bool? AllowPartialPathOverride;

        public PlatformPathRequest(
            Vector3 start,
            Vector3 target,
            bool forceRequest = false,
            bool projectTargetToGround = false,
            float projectionDistance = 1.5f,
            bool? allowPartialPathOverride = null)
        {
            Start = start;
            Target = target;
            ForceRequest = forceRequest;
            ProjectTargetToGround = projectTargetToGround;
            ProjectionDistance = projectionDistance;
            AllowPartialPathOverride = allowPartialPathOverride;
        }

        public bool ShouldAllowPartialPath(bool configDefault)
        {
            return AllowPartialPathOverride ?? configDefault;
        }
    }
}
