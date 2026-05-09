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
        public readonly PlatformPathCompletionKind CompletionKind;

        public PlatformPathResult(
            bool success,
            bool requestStarted,
            PlatformPathFailureReason failureReason,
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path,
            MoveCommand? currentCommand,
            PlatformPathCompletionKind completionKind)
        {
            Success = success;
            RequestStarted = requestStarted;
            FailureReason = failureReason;
            Start = start;
            OriginalTarget = originalTarget;
            ResolvedTarget = resolvedTarget;
            Path = path;
            CurrentCommand = currentCommand;
            CompletionKind = completionKind;
        }

        public static PlatformPathResult Failed(
            PlatformPathFailureReason reason,
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path = null,
            bool requestStarted = true)
        {
            return new PlatformPathResult(
                false,
                requestStarted,
                reason,
                start,
                originalTarget,
                resolvedTarget,
                path,
                null,
                PlatformPathCompletionKind.Failed);
        }

        public static PlatformPathResult Succeeded(
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path,
            MoveCommand? currentCommand)
        {
            return new PlatformPathResult(
                true,
                true,
                PlatformPathFailureReason.None,
                start,
                originalTarget,
                resolvedTarget,
                path,
                currentCommand,
                path != null ? path.CompletionKind : PlatformPathCompletionKind.FullPath);
        }

        public static PlatformPathResult Partial(
            PlatformPathFailureReason reason,
            Vector3 start,
            Vector3 originalTarget,
            Vector3 resolvedTarget,
            Platform2DPath path,
            MoveCommand? currentCommand)
        {
            return new PlatformPathResult(
                false,
                true,
                reason,
                start,
                originalTarget,
                resolvedTarget,
                path,
                currentCommand,
                PlatformPathCompletionKind.Partial);
        }
    }
}
