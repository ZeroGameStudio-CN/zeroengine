using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    public readonly struct PlatformPathResult
    {
        public readonly bool Success;
        public readonly bool RequestStarted;
        public readonly PlatformPathFailureReason FailureReason;
        public readonly Vector3 Start;
        public readonly Vector3 OriginalTarget;
        public readonly Vector3 ResolvedTarget;
        public readonly Platform2DPath Path;
        public readonly MoveCommand? CurrentCommand;

        public PlatformPathResult(
            bool success,
            bool requestStarted,
            PlatformPathFailureReason failureReason,
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path,
            MoveCommand? currentCommand)
        {
            Success = success;
            RequestStarted = requestStarted;
            FailureReason = failureReason;
            Start = start;
            OriginalTarget = originalTarget;
            ResolvedTarget = resolvedTarget;
            Path = path;
            CurrentCommand = currentCommand;
        }

        public static PlatformPathResult Failed(
            PlatformPathFailureReason reason,
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path = null,
            bool requestStarted = true)
        {
            return new PlatformPathResult(false, requestStarted, reason, start, originalTarget, resolvedTarget, path, null);
        }

        public static PlatformPathResult Succeeded(
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path,
            MoveCommand? currentCommand)
        {
            return new PlatformPathResult(true, true, PlatformPathFailureReason.None, start, originalTarget, resolvedTarget, path, currentCommand);
        }
    }
}
