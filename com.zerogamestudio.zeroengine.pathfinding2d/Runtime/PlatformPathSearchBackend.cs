using System;
using System.Threading;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    [Flags]
    public enum PlatformPathSearchBackendCapabilities
    {
        None = 0,
        MultipleTargets = 1 << 0,
        Asynchronous = 1 << 1,
        SingleThreaded = 1 << 2
    }

    public enum PlatformSearchFailureReason
    {
        None,
        InvalidRequest,
        GraphUnavailable,
        StartNodeNotFound,
        TargetNodeNotFound,
        NoPath,
        Cancelled,
        Timeout,
        BackendUnavailable,
        BackendFault,
        StaleGraph
    }

    public enum PlatformSearchSubmissionKind
    {
        Rejected,
        Immediate,
        Pending
    }

    public enum PlatformSearchGraphPreparationKind
    {
        Failed,
        Ready,
        Pending
    }

    public readonly struct PlatformPathSearchCostContext : IEquatable<PlatformPathSearchCostContext>
    {
        public const int CurrentPolicyRevision = 1;

        public readonly int PolicyRevision;
        public readonly bool TargetIsElevated;
        public readonly float StartSurfaceY;
        public readonly float WalkCommandVerticalTolerance;
        public readonly float LowNodePenalty;
        public readonly long Key;

        public bool IsValid =>
            PolicyRevision > 0 &&
            IsFinite(StartSurfaceY) &&
            IsFinite(WalkCommandVerticalTolerance) &&
            WalkCommandVerticalTolerance >= 0f &&
            IsFinite(LowNodePenalty) &&
            LowNodePenalty >= 0f;

        public PlatformPathSearchCostContext(
            bool targetIsElevated,
            float startSurfaceY,
            float walkCommandVerticalTolerance,
            float lowNodePenalty = 1000f,
            int policyRevision = CurrentPolicyRevision)
        {
            PolicyRevision = policyRevision;
            TargetIsElevated = targetIsElevated;
            StartSurfaceY = startSurfaceY;
            WalkCommandVerticalTolerance = walkCommandVerticalTolerance;
            LowNodePenalty = lowNodePenalty;
            Key = BuildKey(
                policyRevision,
                targetIsElevated,
                startSurfaceY,
                walkCommandVerticalTolerance,
                lowNodePenalty);
        }

        public float GetTraversalCost(PlatformSearchLink link, PlatformSearchNode destination)
        {
            float penalty = TargetIsElevated &&
                            destination.Position.y < StartSurfaceY - WalkCommandVerticalTolerance
                ? LowNodePenalty
                : 0f;
            return link.BaseCost + penalty;
        }

        public bool Equals(PlatformPathSearchCostContext other)
        {
            return Key == other.Key &&
                   PolicyRevision == other.PolicyRevision &&
                   TargetIsElevated == other.TargetIsElevated &&
                   StartSurfaceY.Equals(other.StartSurfaceY) &&
                   WalkCommandVerticalTolerance.Equals(other.WalkCommandVerticalTolerance) &&
                   LowNodePenalty.Equals(other.LowNodePenalty);
        }

        public override bool Equals(object obj)
        {
            return obj is PlatformPathSearchCostContext other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        private static long BuildKey(
            int policyRevision,
            bool targetIsElevated,
            float startSurfaceY,
            float tolerance,
            float penalty)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ policyRevision) * 1099511628211L;
                hash = (hash ^ (targetIsElevated ? 1 : 0)) * 1099511628211L;
                hash = (hash ^ BitConverter.SingleToInt32Bits(startSurfaceY)) * 1099511628211L;
                hash = (hash ^ BitConverter.SingleToInt32Bits(tolerance)) * 1099511628211L;
                hash = (hash ^ BitConverter.SingleToInt32Bits(penalty)) * 1099511628211L;
                return hash;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class PlatformSearchRequest
    {
        private readonly int[] targetNodeIds;

        public long RequestRevision { get; }
        public PlatformSearchGraphSnapshot Graph { get; }
        public int SourceNodeId { get; }
        public PlatformPathSearchCostContext CostContext { get; }
        public bool AllowPartialPath { get; }
        public long DeadlineTimestamp { get; }
        public int TargetCount => targetNodeIds.Length;

        public PlatformSearchRequest(
            long requestRevision,
            PlatformSearchGraphSnapshot graph,
            int sourceNodeId,
            int[] targetNodeIds,
            PlatformPathSearchCostContext costContext,
            bool allowPartialPath = false,
            long deadlineTimestamp = 0)
        {
            if (targetNodeIds == null)
                throw new ArgumentNullException(nameof(targetNodeIds));

            RequestRevision = requestRevision;
            Graph = graph;
            SourceNodeId = sourceNodeId;
            this.targetNodeIds = (int[])targetNodeIds.Clone();
            CostContext = costContext;
            AllowPartialPath = allowPartialPath;
            DeadlineTimestamp = deadlineTimestamp;
        }

        public int GetTargetNodeId(int index)
        {
            if ((uint)index >= (uint)targetNodeIds.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return targetNodeIds[index];
        }
    }

    public readonly struct PlatformSearchTargetResult
    {
        private readonly int[] linkIds;

        public readonly int TargetNodeId;
        public readonly int ResolvedNodeId;
        public readonly bool Found;
        public readonly bool IsPartial;
        public readonly float SearchCost;
        public int LinkCount => linkIds?.Length ?? 0;

        public PlatformSearchTargetResult(
            int targetNodeId,
            int resolvedNodeId,
            bool found,
            bool isPartial,
            float searchCost,
            int[] linkIds)
        {
            TargetNodeId = targetNodeId;
            ResolvedNodeId = resolvedNodeId;
            Found = found;
            IsPartial = isPartial;
            SearchCost = searchCost;
            this.linkIds = linkIds == null ? Array.Empty<int>() : (int[])linkIds.Clone();
        }

        public int GetLinkId(int index)
        {
            if ((uint)index >= (uint)LinkCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return linkIds[index];
        }
    }

    public sealed class PlatformSearchResult
    {
        private readonly PlatformSearchTargetResult[] targets;

        public long RequestRevision { get; }
        public long GraphIdentity { get; }
        public long GraphRevision { get; }
        public string BackendId { get; }
        public int BackendRevision { get; }
        public PlatformSearchFailureReason FailureReason { get; }
        public long ExpandedNodes { get; }
        public long OpenSetPeak { get; }
        public int TargetCount => targets.Length;
        public bool Success => FailureReason == PlatformSearchFailureReason.None;

        public PlatformSearchResult(
            long requestRevision,
            long graphIdentity,
            long graphRevision,
            string backendId,
            int backendRevision,
            PlatformSearchFailureReason failureReason,
            PlatformSearchTargetResult[] targets,
            long expandedNodes,
            long openSetPeak)
        {
            RequestRevision = requestRevision;
            GraphIdentity = graphIdentity;
            GraphRevision = graphRevision;
            BackendId = backendId ?? string.Empty;
            BackendRevision = backendRevision;
            FailureReason = failureReason;
            this.targets = targets == null
                ? Array.Empty<PlatformSearchTargetResult>()
                : (PlatformSearchTargetResult[])targets.Clone();
            ExpandedNodes = expandedNodes;
            OpenSetPeak = openSetPeak;
        }

        public PlatformSearchTargetResult GetTarget(int index)
        {
            if ((uint)index >= (uint)targets.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return targets[index];
        }
    }

    public sealed class PlatformPathSearchHandle
    {
        private const int PendingState = 0;
        private const int CompletedState = 1;
        private const int CancelledState = 2;

        private Action cancelAction;
        private int state;

        public long RequestRevision { get; }
        public bool IsPending => Volatile.Read(ref state) == PendingState;
        public bool IsCompleted => Volatile.Read(ref state) == CompletedState;
        public bool IsCancelled => Volatile.Read(ref state) == CancelledState;

        public PlatformPathSearchHandle(long requestRevision)
        {
            RequestRevision = requestRevision;
        }

        public bool Cancel()
        {
            if (Interlocked.CompareExchange(ref state, CancelledState, PendingState) != PendingState)
                return false;

            Interlocked.Exchange(ref cancelAction, null)?.Invoke();
            return true;
        }

        public void SetCancelAction(Action action)
        {
            if (action == null)
                return;

            if (Volatile.Read(ref state) != PendingState)
            {
                if (IsCancelled)
                    action();
                return;
            }

            Interlocked.Exchange(ref cancelAction, action);
            if (IsCancelled)
                Interlocked.Exchange(ref cancelAction, null)?.Invoke();
        }

        public bool TryComplete()
        {
            bool completed = Interlocked.CompareExchange(ref state, CompletedState, PendingState) == PendingState;
            if (completed)
                Interlocked.Exchange(ref cancelAction, null);
            return completed;
        }
    }

    public readonly struct PlatformSearchSubmission
    {
        public readonly PlatformSearchSubmissionKind Kind;
        public readonly PlatformSearchResult ImmediateResult;
        public readonly PlatformPathSearchHandle Handle;

        private PlatformSearchSubmission(
            PlatformSearchSubmissionKind kind,
            PlatformSearchResult immediateResult,
            PlatformPathSearchHandle handle)
        {
            Kind = kind;
            ImmediateResult = immediateResult;
            Handle = handle;
        }

        public static PlatformSearchSubmission Rejected(PlatformSearchResult result)
        {
            return new PlatformSearchSubmission(PlatformSearchSubmissionKind.Rejected, result, null);
        }

        public static PlatformSearchSubmission Immediate(PlatformSearchResult result)
        {
            return new PlatformSearchSubmission(PlatformSearchSubmissionKind.Immediate, result, null);
        }

        public static PlatformSearchSubmission Pending(PlatformPathSearchHandle handle)
        {
            if (handle == null)
                throw new ArgumentNullException(nameof(handle));
            return new PlatformSearchSubmission(PlatformSearchSubmissionKind.Pending, null, handle);
        }
    }

    public readonly struct PlatformSearchGraphPreparation
    {
        public readonly PlatformSearchGraphPreparationKind Kind;
        public readonly long GraphIdentity;
        public readonly long GraphRevision;
        public readonly PlatformSearchFailureReason FailureReason;
        public readonly PlatformPathSearchHandle Handle;

        private PlatformSearchGraphPreparation(
            PlatformSearchGraphPreparationKind kind,
            long graphIdentity,
            long graphRevision,
            PlatformSearchFailureReason failureReason,
            PlatformPathSearchHandle handle)
        {
            Kind = kind;
            GraphIdentity = graphIdentity;
            GraphRevision = graphRevision;
            FailureReason = failureReason;
            Handle = handle;
        }

        public static PlatformSearchGraphPreparation Ready(PlatformSearchGraphSnapshot snapshot)
        {
            return new PlatformSearchGraphPreparation(
                PlatformSearchGraphPreparationKind.Ready,
                snapshot.GraphIdentity,
                snapshot.GraphRevision,
                PlatformSearchFailureReason.None,
                null);
        }

        public static PlatformSearchGraphPreparation Failed(
            PlatformSearchGraphSnapshot snapshot,
            PlatformSearchFailureReason reason)
        {
            return new PlatformSearchGraphPreparation(
                PlatformSearchGraphPreparationKind.Failed,
                snapshot != null ? snapshot.GraphIdentity : 0,
                snapshot != null ? snapshot.GraphRevision : 0,
                reason,
                null);
        }

        public static PlatformSearchGraphPreparation Pending(
            PlatformSearchGraphSnapshot snapshot,
            PlatformPathSearchHandle handle)
        {
            return new PlatformSearchGraphPreparation(
                PlatformSearchGraphPreparationKind.Pending,
                snapshot.GraphIdentity,
                snapshot.GraphRevision,
                PlatformSearchFailureReason.None,
                handle);
        }
    }

    public interface IPlatformPathSearchBackend
    {
        string BackendId { get; }
        int BackendRevision { get; }
        PlatformPathSearchBackendCapabilities Capabilities { get; }

        PlatformSearchGraphPreparation PrepareGraph(
            PlatformSearchGraphSnapshot graph,
            Action<PlatformSearchGraphPreparation> completed = null);

        bool IsGraphReady(long graphIdentity, long graphRevision);

        /// <summary>
        /// Submits one immutable-snapshot request. A Pending callback must run on Unity's main thread,
        /// exactly once after this method has returned, unless the returned handle was cancelled.
        /// Immediate and Rejected submissions do not invoke the callback.
        /// </summary>
        PlatformSearchSubmission Submit(
            PlatformSearchRequest request,
            Action<PlatformSearchResult> completed = null);
    }
}
