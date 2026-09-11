using System;

namespace ZeroEngine.Pathfinding2D
{
    public sealed class PlatformRouteBatchQuery
    {
        private readonly PlatformRouteQuery[] queries;

        public int Count => queries.Length;

        public PlatformRouteBatchQuery(PlatformRouteQuery[] queries)
        {
            if (queries == null)
                throw new ArgumentNullException(nameof(queries));
            this.queries = (PlatformRouteQuery[])queries.Clone();
        }

        public PlatformRouteQuery GetQuery(int index)
        {
            if ((uint)index >= (uint)queries.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return queries[index];
        }
    }

    public sealed class PlatformRouteBatchResult
    {
        private readonly PlatformRouteQueryResult[] results;

        public long RequestRevision { get; }
        public long GraphIdentity { get; }
        public long GraphRevision { get; }
        public string BackendId { get; }
        public int BackendRevision { get; }
        public int Count => results.Length;

        public PlatformRouteBatchResult(
            long requestRevision,
            long graphIdentity,
            long graphRevision,
            string backendId,
            int backendRevision,
            PlatformRouteQueryResult[] results)
        {
            RequestRevision = requestRevision;
            GraphIdentity = graphIdentity;
            GraphRevision = graphRevision;
            BackendId = backendId ?? string.Empty;
            BackendRevision = backendRevision;
            this.results = results == null
                ? Array.Empty<PlatformRouteQueryResult>()
                : (PlatformRouteQueryResult[])results.Clone();
        }

        public PlatformRouteQueryResult GetResult(int index)
        {
            if ((uint)index >= (uint)results.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return results[index];
        }
    }

    public readonly struct PlatformRouteBatchSubmission
    {
        public readonly PlatformSearchSubmissionKind Kind;
        public readonly PlatformRouteBatchResult ImmediateResult;
        public readonly PlatformPathSearchHandle Handle;

        private PlatformRouteBatchSubmission(
            PlatformSearchSubmissionKind kind,
            PlatformRouteBatchResult immediateResult,
            PlatformPathSearchHandle handle)
        {
            Kind = kind;
            ImmediateResult = immediateResult;
            Handle = handle;
        }

        public static PlatformRouteBatchSubmission Rejected(PlatformRouteBatchResult result)
        {
            return new PlatformRouteBatchSubmission(PlatformSearchSubmissionKind.Rejected, result, null);
        }

        public static PlatformRouteBatchSubmission Immediate(PlatformRouteBatchResult result)
        {
            return new PlatformRouteBatchSubmission(PlatformSearchSubmissionKind.Immediate, result, null);
        }

        public static PlatformRouteBatchSubmission Pending(PlatformPathSearchHandle handle)
        {
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            return new PlatformRouteBatchSubmission(PlatformSearchSubmissionKind.Pending, null, handle);
        }
    }
}
