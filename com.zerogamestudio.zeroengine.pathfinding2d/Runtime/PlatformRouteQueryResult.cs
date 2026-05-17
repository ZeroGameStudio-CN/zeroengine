using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformRouteQueryResult
    {
        public readonly bool Success;
        public readonly PlatformPathCompletionKind CompletionKind;
        public readonly PlatformPathFailureReason FailureReason;
        public readonly PlatformRouteCost Cost;
        public readonly Vector3 ResolvedTarget;
        public readonly Platform2DPath Path;
        public readonly string DebugSummary;

        public PlatformRouteQueryResult(
            bool success,
            PlatformPathCompletionKind completionKind,
            PlatformPathFailureReason failureReason,
            PlatformRouteCost cost,
            Vector3 resolvedTarget,
            Platform2DPath path,
            string debugSummary)
        {
            Success = success;
            CompletionKind = completionKind;
            FailureReason = failureReason;
            Cost = cost;
            ResolvedTarget = resolvedTarget;
            Path = path;
            DebugSummary = debugSummary;
        }

        public static PlatformRouteQueryResult Failed(
            PlatformPathFailureReason failureReason,
            Vector3 resolvedTarget,
            string debugSummary)
        {
            return new PlatformRouteQueryResult(
                false,
                PlatformPathCompletionKind.Failed,
                failureReason,
                PlatformRouteCost.Unreachable,
                resolvedTarget,
                null,
                debugSummary);
        }
    }
}
