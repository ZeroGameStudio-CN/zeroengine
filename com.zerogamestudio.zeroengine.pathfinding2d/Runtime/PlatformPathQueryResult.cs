using System;

namespace ZeroEngine.Pathfinding2D
{
    public sealed class PlatformPathQueryResult
    {
        public long RequestRevision { get; }
        public long GraphIdentity { get; }
        public long GraphRevision { get; }
        public string BackendId { get; }
        public int BackendRevision { get; }
        public PlatformPathResult PathResult { get; }
        public long ExpandedNodes { get; }
        public long OpenSetPeak { get; }

        public PlatformPathQueryResult(
            long requestRevision,
            long graphIdentity,
            long graphRevision,
            string backendId,
            int backendRevision,
            PlatformPathResult pathResult,
            long expandedNodes,
            long openSetPeak)
        {
            RequestRevision = requestRevision;
            GraphIdentity = graphIdentity;
            GraphRevision = graphRevision;
            BackendId = backendId ?? string.Empty;
            BackendRevision = backendRevision;
            PathResult = pathResult;
            ExpandedNodes = expandedNodes;
            OpenSetPeak = openSetPeak;
        }
    }

    public readonly struct PlatformPathQuerySubmission
    {
        public readonly PlatformSearchSubmissionKind Kind;
        public readonly PlatformPathQueryResult ImmediateResult;
        public readonly PlatformPathSearchHandle Handle;

        private PlatformPathQuerySubmission(
            PlatformSearchSubmissionKind kind,
            PlatformPathQueryResult immediateResult,
            PlatformPathSearchHandle handle)
        {
            Kind = kind;
            ImmediateResult = immediateResult;
            Handle = handle;
        }

        public static PlatformPathQuerySubmission Rejected(PlatformPathQueryResult result)
        {
            return new PlatformPathQuerySubmission(PlatformSearchSubmissionKind.Rejected, result, null);
        }

        public static PlatformPathQuerySubmission Immediate(PlatformPathQueryResult result)
        {
            return new PlatformPathQuerySubmission(PlatformSearchSubmissionKind.Immediate, result, null);
        }

        public static PlatformPathQuerySubmission Pending(PlatformPathSearchHandle handle)
        {
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));

            return new PlatformPathQuerySubmission(PlatformSearchSubmissionKind.Pending, null, handle);
        }
    }
}
