// Platform2DPathfinder.cs
// 2D 平台寻路器
// 使用生成的平台图进行路径查找，输出 MoveCommand 序列

using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 寻路器配置
    /// </summary>
    [System.Serializable]
    public class PathfinderConfig
    {
        [Header("路径请求")]
        [Tooltip("路径请求间隔（秒）")]
        public float PathRequestInterval = 0.3f;

        [Tooltip("路径过期时间（秒）")]
        public float PathExpireTime = 2f;

        [Tooltip("到达目标的判定距离（高速移动时需要更大值避免过冲）")]
        public float ArriveDistance = 1.0f;

        [Tooltip("行走指令到达判定距离。应小于 ArriveDistance，避免短距离起跳点被第一帧跳过。")]
        public float WalkCommandArriveDistance = 0.25f;

        [Tooltip("衔接 Jump/Fall/DropDown 前的行走到达距离；使用更小值避免提前切换后越过合法起跳点。")]
        public float TraversalApproachArriveDistance = 0.05f;

        [Tooltip("行走指令允许的最大高度差。更大的垂直移动必须由 Jump/Fall/DropDown 表达。")]
        public float WalkCommandVerticalTolerance = 0.5f;

        [Header("节点查找")]
        [Tooltip("查找起点/终点节点的最大距离")]
        public float MaxNodeSearchRadius = 8f;

        [Tooltip("行走速度（用于时间估算）")]
        public float WalkSpeed = 5f;

        [Header("路径验证")]
        [Tooltip("目标移动多远后需要重新寻路")]
        public float TargetMoveThreshold = 2f;

        [Tooltip("偏离路径多远后需要重新寻路")]
        public float PathDeviationThreshold = 3f;

        [Tooltip("启用自动路径验证")]
        public bool AutoValidatePath = true;

        [Header("回退策略")]
        [Tooltip("找不到完整路径时，返回部分路径")]
        public bool AllowPartialPath = true;

        [Tooltip("同平台直接行走的最大高度差")]
        public float SamePlatformMaxHeightDiff = 0.5f;
    }

    /// <summary>
    /// 路径验证结果
    /// </summary>
    public enum PathValidationResult
    {
        /// <summary>路径有效</summary>
        Valid,
        /// <summary>路径已过期</summary>
        Expired,
        /// <summary>目标已移动</summary>
        TargetMoved,
        /// <summary>偏离路径</summary>
        Deviated,
        /// <summary>路径不存在</summary>
        NoPath
    }

    /// <summary>
    /// 2D 平台寻路器
    /// 基于平台图进行 A* 寻路，生成 MoveCommand 序列
    /// </summary>
    public class Platform2DPathfinder : MonoBehaviour
    {
        [SerializeField]
        private PathfinderConfig config = new PathfinderConfig();

        [SerializeField]
        private PlatformGraphGenerator graphGenerator;

        /// <summary>配置</summary>
        public PathfinderConfig Config => config;

        /// <summary>平台图生成器</summary>
        public PlatformGraphGenerator GraphGenerator => graphGenerator;

        /// <summary>当前路径</summary>
        public Platform2DPath CurrentPath { get; private set; }

        /// <summary>是否有有效路径</summary>
        public bool HasValidPath => CurrentPath != null &&
                                    CurrentPath.Status == PathStatus.Valid &&
                                    !CurrentPath.IsComplete;

        /// <summary>是否有完整或已到达路径。部分路径不代表导航成功。</summary>
        public bool HasCompletePath => CurrentPath != null &&
                                       CurrentPath.Status == PathStatus.Valid &&
                                       CurrentPath.CompletionKind != PlatformPathCompletionKind.Partial;

        /// <summary>上一次寻路失败原因</summary>
        public PlatformPathFailureReason LastFailureReason { get; private set; }

        private float lastPathRequestTime = -999f;
        private readonly ManagedPlatformPathSearchBackend managedSearchBackend = new ManagedPlatformPathSearchBackend();
        private IPlatformPathSearchBackend searchBackend;
        private PlatformPathSearchHandle pendingSearchHandle;
        private long nextSearchRequestRevision;
        private long latestSubmittedRequestRevision;
        private long nextRouteBatchRevision;

        private void Awake()
        {
            if (graphGenerator == null)
            {
                graphGenerator = FindObjectOfType<PlatformGraphGenerator>();
            }

            EnsureSearchBackend();
            PrepareSearchBackend();
        }

        private void OnDisable()
        {
            CancelPendingPathQuery();
        }

        private void OnDestroy()
        {
            CancelPendingPathQuery();
            DisposeSearchBackend(searchBackend);
            searchBackend = null;
        }

        /// <summary>
        /// 绑定平台图生成器。
        /// </summary>
        public void SetGraphGenerator(PlatformGraphGenerator generator)
        {
            CancelPendingPathQuery();
            graphGenerator = generator;
            PrepareSearchBackend();
        }

        public IPlatformPathSearchBackend SearchBackend
        {
            get
            {
                EnsureSearchBackend();
                return searchBackend;
            }
        }

        /// <summary>
        /// Selects a runtime-only search backend. Passing null restores the package-owned managed backend.
        /// Existing serialized fields and synchronous APIs are unaffected.
        /// </summary>
        public void SetSearchBackend(IPlatformPathSearchBackend backend)
        {
            IPlatformPathSearchBackend nextBackend = backend ?? managedSearchBackend;
            if (object.ReferenceEquals(searchBackend, nextBackend))
            {
                PrepareSearchBackend();
                return;
            }

            CancelPendingPathQuery();
            IPlatformPathSearchBackend previousBackend = searchBackend;
            searchBackend = nextBackend;
            DisposeSearchBackend(previousBackend);
            PrepareSearchBackend();
        }

        public PlatformSearchGraphPreparation PrepareSearchBackend()
        {
            EnsureSearchBackend();
            PlatformSearchGraphSnapshot snapshot = graphGenerator != null
                ? graphGenerator.SearchSnapshot
                : null;
            return snapshot != null
                ? searchBackend.PrepareGraph(snapshot)
                : PlatformSearchGraphPreparation.Failed(null, PlatformSearchFailureReason.GraphUnavailable);
        }

        /// <summary>
        /// Submits a backend-neutral semantic path query without changing CurrentPath.
        /// Pending callbacks are delivered only by the selected backend after this method returns.
        /// </summary>
        public PlatformPathQuerySubmission SubmitPathQuery(
            PlatformPathRequest request,
            System.Action<PlatformPathQueryResult> completed = null)
        {
            EnsureSearchBackend();
            pendingSearchHandle?.Cancel();
            pendingSearchHandle = null;

            long requestRevision = ++nextSearchRequestRevision;
            latestSubmittedRequestRevision = requestRevision;
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.RequestSubmitted);

            if (!request.ForceRequest && Time.time - lastPathRequestTime < config.PathRequestInterval)
            {
                PlatformPathResult throttled = PlatformPathResult.Failed(
                    PlatformPathFailureReason.Throttled,
                    request.Start,
                    request.Target,
                    request.Target,
                    CurrentPath,
                    requestStarted: false);
                PlatformPathfindingDiagnostics.RecordPathResult(throttled);
                return PlatformPathQuerySubmission.Rejected(CreatePathQueryResult(requestRevision, throttled));
            }

            if (graphGenerator == null)
            {
                PlatformPathResult missingGraph = PlatformPathResult.Failed(
                    PlatformPathFailureReason.MissingGraphGenerator,
                    request.Start,
                    request.Target,
                    request.Target,
                    requestStarted: false);
                PlatformPathfindingDiagnostics.RecordPathResult(missingGraph);
                return PlatformPathQuerySubmission.Rejected(CreatePathQueryResult(requestRevision, missingGraph));
            }

            if (!graphGenerator.IsGenerated)
            {
                Platform2DPath notFound = Platform2DPath.NotFound(request.Start, request.Target);
                PlatformPathResult graphNotGenerated = PlatformPathResult.Failed(
                    PlatformPathFailureReason.GraphNotGenerated,
                    request.Start,
                    request.Target,
                    request.Target,
                    notFound,
                    requestStarted: false);
                PlatformPathfindingDiagnostics.RecordPathResult(graphNotGenerated);
                return PlatformPathQuerySubmission.Rejected(CreatePathQueryResult(requestRevision, graphNotGenerated));
            }

            PlatformSearchGraphSnapshot snapshot = graphGenerator.SearchSnapshot;
            if (snapshot == null)
            {
                PlatformPathResult unavailable = PlatformPathResult.Failed(
                    PlatformPathFailureReason.BackendUnavailable,
                    request.Start,
                    request.Target,
                    request.Target,
                    CurrentPath,
                    requestStarted: false);
                PlatformPathfindingDiagnostics.RecordPathResult(unavailable);
                return PlatformPathQuerySubmission.Rejected(CreatePathQueryResult(requestRevision, unavailable));
            }

            lastPathRequestTime = Time.time;
            Vector3 resolvedTarget = request.Target;
            if (request.ProjectTargetToGround)
            {
                resolvedTarget = PlatformTargetProjection.ProjectTargetToGround(
                    request.Target,
                    graphGenerator.Config.AllPlatformLayers,
                    request.ProjectionDistance);
            }

            if (Vector2.Distance(request.Start, resolvedTarget) <= config.ArriveDistance)
            {
                Platform2DPath arrived = Platform2DPath.Arrived(request.Start, resolvedTarget);
                ApplyRouteDiagnostics(arrived, commandsValidated: true);
                PlatformPathResult arrivedResult = PlatformPathResult.Succeeded(
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    arrived,
                    null);
                PlatformPathfindingDiagnostics.RecordPathResult(arrivedResult);
                return PlatformPathQuerySubmission.Immediate(CreatePathQueryResult(requestRevision, arrivedResult));
            }

            Platform2DPath samePlatformPath = TryCreateSamePlatformPath(request.Start, resolvedTarget);
            if (samePlatformPath != null)
            {
                ApplyRouteDiagnostics(samePlatformPath, commandsValidated: true);
                PlatformPathResult samePlatformResult = PlatformPathResult.Succeeded(
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    samePlatformPath,
                    samePlatformPath.GetCurrentCommand());
                PlatformPathfindingDiagnostics.RecordPathResult(samePlatformResult);
                return PlatformPathQuerySubmission.Immediate(CreatePathQueryResult(requestRevision, samePlatformResult));
            }

            Collider2D endPlatform = DetectPlatformBelow(resolvedTarget);
            PlatformNodeData? startNode = ResolveStartNode(request.Start);
            PlatformNodeData? endNode = graphGenerator.FindNearestNodeOnPlatform(
                resolvedTarget,
                endPlatform,
                config.MaxNodeSearchRadius);
            if (!startNode.HasValue)
            {
                PlatformPathResult noStart = PlatformPathResult.Failed(
                    PlatformPathFailureReason.StartNodeNotFound,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    Platform2DPath.NotFound(request.Start, resolvedTarget));
                PlatformPathfindingDiagnostics.RecordPathResult(noStart);
                return PlatformPathQuerySubmission.Immediate(CreatePathQueryResult(requestRevision, noStart));
            }

            if (!endNode.HasValue)
            {
                PlatformPathResult noEnd = PlatformPathResult.Failed(
                    PlatformPathFailureReason.EndNodeNotFound,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    Platform2DPath.NotFound(request.Start, resolvedTarget));
                PlatformPathfindingDiagnostics.RecordPathResult(noEnd);
                return PlatformPathQuerySubmission.Immediate(CreatePathQueryResult(requestRevision, noEnd));
            }

            List<PlatformNodeData> candidates = BuildTargetNodeCandidates(endNode.Value, resolvedTarget);
            var targetNodeIds = new int[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
                targetNodeIds[i] = candidates[i].NodeId;

            bool targetIsElevated = resolvedTarget.y >
                                    startNode.Value.Position.y + config.WalkCommandVerticalTolerance;
            var costContext = new PlatformPathSearchCostContext(
                targetIsElevated,
                startNode.Value.Position.y,
                config.WalkCommandVerticalTolerance);
            var searchRequest = new PlatformSearchRequest(
                requestRevision,
                snapshot,
                startNode.Value.NodeId,
                targetNodeIds,
                costContext,
                request.ShouldAllowPartialPath(config.AllowPartialPath));

            if (!searchBackend.IsGraphReady(snapshot.GraphIdentity, snapshot.GraphRevision))
            {
                PlatformSearchGraphPreparation preparation = searchBackend.PrepareGraph(snapshot);
                if (preparation.Kind != PlatformSearchGraphPreparationKind.Ready)
                {
                    PlatformPathResult unavailable = PlatformPathResult.Failed(
                        PlatformPathFailureReason.BackendUnavailable,
                        request.Start,
                        request.Target,
                        resolvedTarget,
                        CurrentPath,
                        requestStarted: false);
                    PlatformPathfindingDiagnostics.RecordPathResult(unavailable);
                    return PlatformPathQuerySubmission.Rejected(CreatePathQueryResult(requestRevision, unavailable));
                }
            }

            PlatformSearchSubmission backendSubmission = searchBackend.Submit(
                searchRequest,
                searchResult =>
                {
                    PlatformPathQueryResult queryResult = ConvertSearchResult(
                        requestRevision,
                        request,
                        resolvedTarget,
                        startNode.Value,
                        searchResult);
                    if (pendingSearchHandle != null &&
                        pendingSearchHandle.RequestRevision == searchResult.RequestRevision)
                    {
                        pendingSearchHandle = null;
                    }
                    completed?.Invoke(queryResult);
                });

            switch (backendSubmission.Kind)
            {
                case PlatformSearchSubmissionKind.Immediate:
                {
                    PlatformPathQueryResult queryResult = ConvertSearchResult(
                        requestRevision,
                        request,
                        resolvedTarget,
                        startNode.Value,
                        backendSubmission.ImmediateResult);
                    return PlatformPathQuerySubmission.Immediate(queryResult);
                }
                case PlatformSearchSubmissionKind.Pending:
                    pendingSearchHandle = backendSubmission.Handle;
                    return PlatformPathQuerySubmission.Pending(backendSubmission.Handle);
                default:
                {
                    PlatformPathQueryResult queryResult = ConvertSearchResult(
                        requestRevision,
                        request,
                        resolvedTarget,
                        startNode.Value,
                        backendSubmission.ImmediateResult);
                    return PlatformPathQuerySubmission.Rejected(queryResult);
                }
            }
        }

        /// <summary>
        /// Commits a completed query only when it still belongs to the latest request, graph and backend.
        /// The caller chooses the gameplay-safe commit boundary.
        /// </summary>
        public bool TryCommitPathQueryResult(PlatformPathQueryResult queryResult)
        {
            if (queryResult == null ||
                graphGenerator == null ||
                !graphGenerator.IsGenerated ||
                graphGenerator.IsBuildInProgress ||
                graphGenerator.SearchSnapshot == null)
                return false;

            EnsureSearchBackend();
            if (queryResult.RequestRevision != latestSubmittedRequestRevision ||
                queryResult.GraphIdentity != graphGenerator.GraphIdentity ||
                queryResult.GraphRevision != graphGenerator.GraphRevision ||
                queryResult.BackendId != searchBackend.BackendId ||
                queryResult.BackendRevision != searchBackend.BackendRevision)
            {
                return false;
            }

            Platform2DPath committedPath = queryResult.PathResult.CompletionKind ==
                                           PlatformPathCompletionKind.Failed
                ? null
                : queryResult.PathResult.Path;
            CommitPathState(
                committedPath,
                queryResult.PathResult.FailureReason,
                mutateCurrentPath: true);
            return true;
        }

        public bool CancelPendingPathQuery()
        {
            PlatformPathSearchHandle handle = pendingSearchHandle;
            pendingSearchHandle = null;
            if (handle == null)
                return false;

            latestSubmittedRequestRevision = ++nextSearchRequestRevision;
            return handle.Cancel();
        }

        private void EnsureSearchBackend()
        {
            if (searchBackend == null)
                searchBackend = managedSearchBackend;
        }

        private static void DisposeSearchBackend(IPlatformPathSearchBackend backend)
        {
            if (backend is System.IDisposable disposable)
                disposable.Dispose();
        }

        private PlatformPathQueryResult CreatePathQueryResult(
            long requestRevision,
            PlatformPathResult pathResult,
            long expandedNodes = 0,
            long openSetPeak = 0)
        {
            PlatformSearchGraphSnapshot snapshot = graphGenerator != null
                ? graphGenerator.SearchSnapshot
                : null;
            return new PlatformPathQueryResult(
                requestRevision,
                snapshot?.GraphIdentity ?? 0,
                snapshot?.GraphRevision ?? 0,
                searchBackend?.BackendId ?? string.Empty,
                searchBackend?.BackendRevision ?? 0,
                pathResult,
                expandedNodes,
                openSetPeak);
        }

        private PlatformPathQueryResult ConvertSearchResult(
            long expectedRequestRevision,
            PlatformPathRequest request,
            Vector3 resolvedTarget,
            PlatformNodeData startNode,
            PlatformSearchResult searchResult)
        {
            if (searchResult == null)
            {
                PlatformPathResult fault = PlatformPathResult.Failed(
                    PlatformPathFailureReason.BackendFault,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    CurrentPath);
                PlatformPathfindingDiagnostics.RecordPathResult(fault);
                return CreatePathQueryResult(expectedRequestRevision, fault);
            }

            PlatformSearchGraphSnapshot snapshot = graphGenerator != null
                ? graphGenerator.SearchSnapshot
                : null;
            if (snapshot == null ||
                searchResult.GraphIdentity != snapshot.GraphIdentity ||
                searchResult.GraphRevision != snapshot.GraphRevision ||
                searchResult.RequestRevision != expectedRequestRevision ||
                expectedRequestRevision != latestSubmittedRequestRevision)
            {
                PlatformPathResult stale = PlatformPathResult.Failed(
                    PlatformPathFailureReason.StaleResult,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    CurrentPath);
                PlatformPathfindingDiagnostics.RecordPathResult(stale);
                return new PlatformPathQueryResult(
                    searchResult.RequestRevision,
                    searchResult.GraphIdentity,
                    searchResult.GraphRevision,
                    searchResult.BackendId,
                    searchResult.BackendRevision,
                    stale,
                    searchResult.ExpandedNodes,
                    searchResult.OpenSetPeak);
            }

            if (searchResult.FailureReason != PlatformSearchFailureReason.None)
            {
                PlatformPathResult failed = PlatformPathResult.Failed(
                    MapSearchFailure(searchResult.FailureReason, request),
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    Platform2DPath.NotFound(request.Start, resolvedTarget));
                PlatformPathfindingDiagnostics.RecordPathResult(failed);
                return CreatePathQueryResult(
                    searchResult.RequestRevision,
                    failed,
                    searchResult.ExpandedNodes,
                    searchResult.OpenSetPeak);
            }

            Platform2DPath bestPath = null;
            float bestScore = float.PositiveInfinity;
            bool hasPartial = false;
            PlatformSearchTargetResult firstPartial = default;
            for (int i = 0; i < searchResult.TargetCount; i++)
            {
                PlatformSearchTargetResult targetResult = searchResult.GetTarget(i);
                if (!targetResult.Found)
                    continue;

                if (targetResult.IsPartial)
                {
                    if (!hasPartial)
                    {
                        firstPartial = targetResult;
                        hasPartial = true;
                    }
                    continue;
                }

                Platform2DPath candidatePath = ReconstructSearchPath(
                    snapshot,
                    searchResult.GraphRevision,
                    startNode.NodeId,
                    targetResult,
                    request.Start,
                    resolvedTarget);
                if (candidatePath == null || candidatePath.Status != PathStatus.Valid ||
                    IsInvalidElevatedPath(candidatePath, request.Start, resolvedTarget) ||
                    !ValidatePathCommands(candidatePath, out _))
                {
                    continue;
                }

                float score = ScoreTargetPath(candidatePath, resolvedTarget);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPath = candidatePath;
                }
            }

            PlatformPathResult pathResult;
            if (bestPath != null)
            {
                ApplyRouteDiagnostics(bestPath, commandsValidated: true);
                pathResult = PlatformPathResult.Succeeded(
                    request.Start,
                    request.Target,
                    bestPath.EndPosition,
                    bestPath,
                    bestPath.GetCurrentCommand());
            }
            else if (hasPartial && request.ShouldAllowPartialPath(config.AllowPartialPath) &&
                     snapshot.TryGetNodeIndex(firstPartial.ResolvedNodeId, out int partialNodeIndex))
            {
                Vector3 partialEnd = snapshot.GetNode(partialNodeIndex).Position;
                Platform2DPath partialPath = ReconstructSearchPath(
                    snapshot,
                    searchResult.GraphRevision,
                    startNode.NodeId,
                    firstPartial,
                    request.Start,
                    partialEnd);
                partialPath = ToPartialPath(partialPath);
                if (partialPath != null &&
                    !IsBadElevatedPartialPath(partialPath, request.Start, resolvedTarget) &&
                    ValidatePathCommands(partialPath, out _))
                {
                    ApplyRouteDiagnostics(partialPath, commandsValidated: true);
                    pathResult = PlatformPathResult.Partial(
                        PlatformPathFailureReason.PartialPath,
                        request.Start,
                        request.Target,
                        partialPath.EndPosition,
                        partialPath,
                        partialPath.GetCurrentCommand());
                }
                else
                {
                    pathResult = PlatformPathResult.Failed(
                        PlatformPathFailureReason.PartialPathUnavailable,
                        request.Start,
                        request.Target,
                        resolvedTarget,
                        Platform2DPath.NotFound(request.Start, resolvedTarget));
                }
            }
            else
            {
                pathResult = PlatformPathResult.Failed(
                    request.ShouldAllowPartialPath(config.AllowPartialPath)
                        ? PlatformPathFailureReason.PartialPathUnavailable
                        : PlatformPathFailureReason.PathNotFound,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    Platform2DPath.NotFound(request.Start, resolvedTarget));
            }

            PlatformPathfindingDiagnostics.RecordPathResult(pathResult);
            return new PlatformPathQueryResult(
                searchResult.RequestRevision,
                searchResult.GraphIdentity,
                searchResult.GraphRevision,
                searchResult.BackendId,
                searchResult.BackendRevision,
                pathResult,
                searchResult.ExpandedNodes,
                searchResult.OpenSetPeak);
        }

        private Platform2DPath ReconstructSearchPath(
            PlatformSearchGraphSnapshot snapshot,
            long graphRevision,
            int sourceNodeId,
            PlatformSearchTargetResult targetResult,
            Vector3 actualStart,
            Vector3 actualEnd)
        {
            var cameFrom = new Dictionary<int, int>(targetResult.LinkCount);
            var cameFromLink = new Dictionary<int, PlatformLinkData>(targetResult.LinkCount);
            int currentNodeId = sourceNodeId;
            for (int i = 0; i < targetResult.LinkCount; i++)
            {
                int linkId = targetResult.GetLinkId(i);
                if ((uint)linkId >= (uint)snapshot.LinkCount ||
                    !graphGenerator.TryGetCommittedLink(graphRevision, linkId, out PlatformLinkData binding) ||
                    binding.FromNodeId != currentNodeId)
                {
                    return Platform2DPath.NotFound(actualStart, actualEnd);
                }

                PlatformSearchLink searchLink = snapshot.GetLink(linkId);
                if (searchLink.FromNodeId != binding.FromNodeId ||
                    searchLink.ToNodeId != binding.ToNodeId)
                {
                    return Platform2DPath.NotFound(actualStart, actualEnd);
                }

                cameFrom[binding.ToNodeId] = binding.FromNodeId;
                cameFromLink[binding.ToNodeId] = binding;
                currentNodeId = binding.ToNodeId;
            }

            if (currentNodeId != targetResult.ResolvedNodeId)
                return Platform2DPath.NotFound(actualStart, actualEnd);

            return ReconstructPath(
                cameFrom,
                cameFromLink,
                currentNodeId,
                actualStart,
                actualEnd);
        }

        private PlatformPathFailureReason MapSearchFailure(
            PlatformSearchFailureReason failureReason,
            PlatformPathRequest request)
        {
            switch (failureReason)
            {
                case PlatformSearchFailureReason.StartNodeNotFound:
                    return PlatformPathFailureReason.StartNodeNotFound;
                case PlatformSearchFailureReason.TargetNodeNotFound:
                    return PlatformPathFailureReason.EndNodeNotFound;
                case PlatformSearchFailureReason.NoPath:
                    return request.ShouldAllowPartialPath(config.AllowPartialPath)
                        ? PlatformPathFailureReason.PartialPathUnavailable
                        : PlatformPathFailureReason.PathNotFound;
                case PlatformSearchFailureReason.Cancelled:
                    return PlatformPathFailureReason.Cancelled;
                case PlatformSearchFailureReason.Timeout:
                    return PlatformPathFailureReason.BackendTimeout;
                case PlatformSearchFailureReason.StaleGraph:
                    return PlatformPathFailureReason.StaleResult;
                case PlatformSearchFailureReason.BackendFault:
                    return PlatformPathFailureReason.BackendFault;
                default:
                    return PlatformPathFailureReason.BackendUnavailable;
            }
        }

        private void ApplyRouteBatchGroupResult(
            PlatformSearchGraphSnapshot snapshot,
            PlatformNodeData startNode,
            RouteBatchGroup group,
            PlatformSearchResult searchResult,
            PlatformRouteQueryResult[] results,
            bool[] resultAssigned)
        {
            bool stale = searchResult == null ||
                         searchResult.GraphIdentity != snapshot.GraphIdentity ||
                         searchResult.GraphRevision != snapshot.GraphRevision ||
                         graphGenerator == null ||
                         graphGenerator.SearchSnapshot != snapshot;
            if (stale || searchResult.FailureReason != PlatformSearchFailureReason.None)
            {
                PlatformPathFailureReason reason = stale
                    ? PlatformPathFailureReason.StaleResult
                    : MapRouteSearchFailure(searchResult.FailureReason, group.AllowPartialPath);
                for (int itemIndex = 0; itemIndex < group.Items.Count; itemIndex++)
                {
                    RouteBatchItem item = group.Items[itemIndex];
                    results[item.QueryIndex] = PlatformRouteQueryResult.Failed(
                        reason,
                        item.ResolvedTarget,
                        $"RouteBatchSearchFailure={reason}");
                    resultAssigned[item.QueryIndex] = true;
                }
                return;
            }

            for (int itemIndex = 0; itemIndex < group.Items.Count; itemIndex++)
            {
                RouteBatchItem item = group.Items[itemIndex];
                Platform2DPath bestPath = null;
                float bestScore = float.PositiveInfinity;
                bool hasPartial = false;
                PlatformSearchTargetResult firstPartial = default;
                int targetEnd = item.TargetStart + item.TargetCount;
                if (item.TargetStart < 0 || targetEnd > searchResult.TargetCount)
                {
                    results[item.QueryIndex] = PlatformRouteQueryResult.Failed(
                        PlatformPathFailureReason.BackendFault,
                        item.ResolvedTarget,
                        "RouteBatchTargetCountMismatch");
                    resultAssigned[item.QueryIndex] = true;
                    continue;
                }

                for (int targetIndex = item.TargetStart; targetIndex < targetEnd; targetIndex++)
                {
                    PlatformSearchTargetResult targetResult = searchResult.GetTarget(targetIndex);
                    if (!targetResult.Found)
                        continue;
                    if (targetResult.IsPartial)
                    {
                        if (!hasPartial)
                        {
                            firstPartial = targetResult;
                            hasPartial = true;
                        }
                        continue;
                    }

                    Platform2DPath candidatePath = ReconstructSearchPath(
                        snapshot,
                        searchResult.GraphRevision,
                        startNode.NodeId,
                        targetResult,
                        item.Query.StartPosition,
                        item.ResolvedTarget);
                    if (candidatePath == null || candidatePath.Status != PathStatus.Valid ||
                        IsInvalidElevatedPath(candidatePath, item.Query.StartPosition, item.ResolvedTarget) ||
                        !ValidatePathCommands(candidatePath, out _))
                    {
                        continue;
                    }

                    float score = ScoreTargetPath(candidatePath, item.ResolvedTarget);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPath = candidatePath;
                    }
                }

                if (bestPath != null)
                {
                    ApplyRouteDiagnostics(bestPath, commandsValidated: true);
                    PlatformPathResult pathResult = PlatformPathResult.Succeeded(
                        item.Query.StartPosition,
                        item.Query.TargetPosition,
                        bestPath.EndPosition,
                        bestPath,
                        bestPath.GetCurrentCommand());
                    PlatformRouteCost cost = CalculateRouteCost(bestPath);
                    results[item.QueryIndex] = new PlatformRouteQueryResult(
                        true,
                        bestPath.CompletionKind,
                        PlatformPathFailureReason.None,
                        cost,
                        bestPath.EndPosition,
                        bestPath,
                        BuildRouteDebugSummary(pathResult, cost));
                    resultAssigned[item.QueryIndex] = true;
                    continue;
                }

                if (hasPartial && group.AllowPartialPath &&
                    snapshot.TryGetNodeIndex(firstPartial.ResolvedNodeId, out int partialNodeIndex))
                {
                    Vector3 partialEnd = snapshot.GetNode(partialNodeIndex).Position;
                    Platform2DPath partialPath = ReconstructSearchPath(
                        snapshot,
                        searchResult.GraphRevision,
                        startNode.NodeId,
                        firstPartial,
                        item.Query.StartPosition,
                        partialEnd);
                    partialPath = ToPartialPath(partialPath);
                    if (partialPath != null &&
                        !IsBadElevatedPartialPath(partialPath, item.Query.StartPosition, item.ResolvedTarget) &&
                        ValidatePathCommands(partialPath, out _))
                    {
                        ApplyRouteDiagnostics(partialPath, commandsValidated: true);
                        PlatformPathResult partialResult = PlatformPathResult.Partial(
                            PlatformPathFailureReason.PartialPath,
                            item.Query.StartPosition,
                            item.Query.TargetPosition,
                            partialPath.EndPosition,
                            partialPath,
                            partialPath.GetCurrentCommand());
                        PlatformRouteCost partialCost = CalculateRouteCost(partialPath);
                        results[item.QueryIndex] = new PlatformRouteQueryResult(
                            false,
                            PlatformPathCompletionKind.Partial,
                            PlatformPathFailureReason.PartialPath,
                            partialCost,
                            partialPath.EndPosition,
                            partialPath,
                            BuildRouteDebugSummary(partialResult, partialCost));
                        resultAssigned[item.QueryIndex] = true;
                        continue;
                    }
                }

                PlatformPathFailureReason unavailableReason = group.AllowPartialPath
                    ? PlatformPathFailureReason.PartialPathUnavailable
                    : PlatformPathFailureReason.PathNotFound;
                results[item.QueryIndex] = PlatformRouteQueryResult.Failed(
                    unavailableReason,
                    item.ResolvedTarget,
                    $"RouteBatchSearchFailure={unavailableReason}");
                resultAssigned[item.QueryIndex] = true;
            }
        }

        private PlatformRouteBatchResult CreateRouteBatchResult(
            long batchRevision,
            PlatformSearchGraphSnapshot snapshot,
            PlatformRouteQueryResult[] results)
        {
            return new PlatformRouteBatchResult(
                batchRevision,
                snapshot?.GraphIdentity ?? 0,
                snapshot?.GraphRevision ?? 0,
                searchBackend?.BackendId ?? string.Empty,
                searchBackend?.BackendRevision ?? 0,
                results);
        }

        private PlatformRouteBatchResult CreateFailedRouteBatch(
            long batchRevision,
            PlatformRouteBatchQuery batch,
            PlatformPathFailureReason reason,
            string summary)
        {
            int count = batch?.Count ?? 0;
            var results = new PlatformRouteQueryResult[count];
            for (int i = 0; i < count; i++)
            {
                PlatformRouteQuery query = batch.GetQuery(i);
                results[i] = PlatformRouteQueryResult.Failed(reason, query.TargetPosition, summary);
            }

            return CreateRouteBatchResult(
                batchRevision,
                graphGenerator != null ? graphGenerator.SearchSnapshot : null,
                results);
        }

        private static PlatformPathFailureReason MapRouteSearchFailure(
            PlatformSearchFailureReason reason,
            bool allowPartialPath)
        {
            switch (reason)
            {
                case PlatformSearchFailureReason.NoPath:
                    return allowPartialPath
                        ? PlatformPathFailureReason.PartialPathUnavailable
                        : PlatformPathFailureReason.PathNotFound;
                case PlatformSearchFailureReason.Cancelled:
                    return PlatformPathFailureReason.Cancelled;
                case PlatformSearchFailureReason.Timeout:
                    return PlatformPathFailureReason.BackendTimeout;
                case PlatformSearchFailureReason.StaleGraph:
                    return PlatformPathFailureReason.StaleResult;
                case PlatformSearchFailureReason.BackendFault:
                    return PlatformPathFailureReason.BackendFault;
                case PlatformSearchFailureReason.StartNodeNotFound:
                    return PlatformPathFailureReason.StartNodeNotFound;
                case PlatformSearchFailureReason.TargetNodeNotFound:
                    return PlatformPathFailureReason.EndNodeNotFound;
                default:
                    return PlatformPathFailureReason.BackendUnavailable;
            }
        }

        private readonly struct RouteBatchGroupKey : System.IEquatable<RouteBatchGroupKey>
        {
            private readonly PlatformPathSearchCostContext costContext;
            private readonly bool allowPartialPath;

            public RouteBatchGroupKey(
                PlatformPathSearchCostContext costContext,
                bool allowPartialPath)
            {
                this.costContext = costContext;
                this.allowPartialPath = allowPartialPath;
            }

            public bool Equals(RouteBatchGroupKey other)
            {
                return allowPartialPath == other.allowPartialPath && costContext.Equals(other.costContext);
            }

            public override bool Equals(object obj)
            {
                return obj is RouteBatchGroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (costContext.GetHashCode() * 397) ^ (allowPartialPath ? 1 : 0);
                }
            }
        }

        private sealed class RouteBatchGroup
        {
            public readonly PlatformPathSearchCostContext CostContext;
            public readonly bool AllowPartialPath;
            public readonly List<int> TargetNodeIds = new List<int>();
            public readonly List<RouteBatchItem> Items = new List<RouteBatchItem>();

            public RouteBatchGroup(
                PlatformPathSearchCostContext costContext,
                bool allowPartialPath)
            {
                CostContext = costContext;
                AllowPartialPath = allowPartialPath;
            }
        }

        private readonly struct RouteBatchItem
        {
            public readonly int QueryIndex;
            public readonly PlatformRouteQuery Query;
            public readonly Vector3 ResolvedTarget;
            public readonly int TargetStart;
            public readonly int TargetCount;

            public RouteBatchItem(
                int queryIndex,
                PlatformRouteQuery query,
                Vector3 resolvedTarget,
                int targetStart,
                int targetCount)
            {
                QueryIndex = queryIndex;
                Query = query;
                ResolvedTarget = resolvedTarget;
                TargetStart = targetStart;
                TargetCount = targetCount;
            }
        }

        /// <summary>
        /// 请求新路径
        /// </summary>
        /// <param name="start">起点位置</param>
        /// <param name="end">终点位置</param>
        /// <param name="forceRequest">强制请求（忽略间隔限制）</param>
        /// <returns>是否成功发起请求</returns>
        public bool RequestPath(Vector3 start, Vector3 end, bool forceRequest = false)
        {
            return TryRequestPath(new PlatformPathRequest(start, end, forceRequest), out _);
        }

        /// <summary>
        /// 请求新路径，并返回可诊断的结果。
        /// </summary>
        public bool TryRequestPath(PlatformPathRequest request, out PlatformPathResult result)
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.RequestSubmitted);
            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.PathRequest))
            {
                bool success = TryCreatePathResult(request, mutateCurrentPath: true, out result);
                PlatformPathfindingDiagnostics.RecordPathResult(result);
                return success;
            }
        }

        public bool TryEvaluateRoute(PlatformRouteQuery query, out PlatformRouteQueryResult result)
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.RequestSubmitted);
            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.RouteEvaluation))
            {
                var request = new PlatformPathRequest(
                    query.StartPosition,
                    query.TargetPosition,
                    forceRequest: true,
                    projectTargetToGround: query.ProjectTargetToGround,
                    allowPartialPathOverride: query.AllowPartialPath);

                bool success = TryCreatePathResult(request, mutateCurrentPath: false, out var pathResult);
                PlatformPathfindingDiagnostics.RecordPathResult(pathResult);
                if (!success || !pathResult.Success || pathResult.Path == null)
                {
                    result = PlatformRouteQueryResult.Failed(
                        pathResult.FailureReason,
                        pathResult.ResolvedTarget,
                        BuildRouteDebugSummary(pathResult, PlatformRouteCost.Unreachable));
                    return false;
                }

                var cost = CalculateRouteCost(pathResult.Path);
                result = new PlatformRouteQueryResult(
                    true,
                    pathResult.CompletionKind,
                    pathResult.FailureReason,
                    cost,
                    pathResult.ResolvedTarget,
                    pathResult.Path,
                    BuildRouteDebugSummary(pathResult, cost));
                return true;
            }
        }

        /// <summary>
        /// Evaluates compatible same-source routes through backend multi-target search without changing CurrentPath.
        /// Queries with different search-cost contexts are separated into independent backend groups.
        /// </summary>
        public PlatformRouteBatchSubmission SubmitRouteBatch(
            PlatformRouteBatchQuery batch,
            System.Action<PlatformRouteBatchResult> completed = null)
        {
            EnsureSearchBackend();
            long batchRevision = ++nextRouteBatchRevision;
            if (batch == null || batch.Count == 0)
                return PlatformRouteBatchSubmission.Rejected(CreateFailedRouteBatch(
                    batchRevision,
                    batch,
                    PlatformPathFailureReason.InvalidCommand,
                    "EmptyRouteBatch"));

            PlatformPathfindingDiagnostics.Add(
                PlatformPathfindingCounterKind.RequestSubmitted,
                batch.Count);

            PlatformSearchGraphSnapshot snapshot = graphGenerator != null
                ? graphGenerator.SearchSnapshot
                : null;
            if (graphGenerator == null || !graphGenerator.IsGenerated || snapshot == null)
            {
                return PlatformRouteBatchSubmission.Rejected(CreateFailedRouteBatch(
                    batchRevision,
                    batch,
                    graphGenerator == null
                        ? PlatformPathFailureReason.MissingGraphGenerator
                        : PlatformPathFailureReason.GraphNotGenerated,
                    "RouteBatchGraphUnavailable"));
            }

            PlatformRouteQuery firstQuery = batch.GetQuery(0);
            PlatformNodeData? startNode = ResolveStartNode(firstQuery.StartPosition);
            if (!startNode.HasValue)
            {
                return PlatformRouteBatchSubmission.Immediate(CreateFailedRouteBatch(
                    batchRevision,
                    batch,
                    PlatformPathFailureReason.StartNodeNotFound,
                    "RouteBatchStartNodeNotFound"));
            }

            var routeResults = new PlatformRouteQueryResult[batch.Count];
            var resultAssigned = new bool[batch.Count];
            var groups = new Dictionary<RouteBatchGroupKey, RouteBatchGroup>();
            for (int queryIndex = 0; queryIndex < batch.Count; queryIndex++)
            {
                PlatformRouteQuery query = batch.GetQuery(queryIndex);
                if (Vector2.Distance(query.StartPosition, firstQuery.StartPosition) > 0.001f)
                {
                    routeResults[queryIndex] = PlatformRouteQueryResult.Failed(
                        PlatformPathFailureReason.InvalidCommand,
                        query.TargetPosition,
                        "RouteBatchRequiresSameSource");
                    resultAssigned[queryIndex] = true;
                    continue;
                }

                Vector3 resolvedTarget = query.ProjectTargetToGround
                    ? PlatformTargetProjection.ProjectTargetToGround(
                        query.TargetPosition,
                        graphGenerator.Config.AllPlatformLayers,
                        1.5f)
                    : query.TargetPosition;

                if (Vector2.Distance(query.StartPosition, resolvedTarget) <= config.ArriveDistance)
                {
                    Platform2DPath arrived = Platform2DPath.Arrived(query.StartPosition, resolvedTarget);
                    ApplyRouteDiagnostics(arrived, commandsValidated: true);
                    PlatformPathResult arrivedPathResult = PlatformPathResult.Succeeded(
                        query.StartPosition,
                        query.TargetPosition,
                        resolvedTarget,
                        arrived,
                        null);
                    PlatformRouteCost arrivedCost = CalculateRouteCost(arrived);
                    routeResults[queryIndex] = new PlatformRouteQueryResult(
                        true,
                        arrived.CompletionKind,
                        PlatformPathFailureReason.None,
                        arrivedCost,
                        resolvedTarget,
                        arrived,
                        BuildRouteDebugSummary(arrivedPathResult, arrivedCost));
                    resultAssigned[queryIndex] = true;
                    continue;
                }

                Platform2DPath samePlatformPath = TryCreateSamePlatformPath(query.StartPosition, resolvedTarget);
                if (samePlatformPath != null)
                {
                    ApplyRouteDiagnostics(samePlatformPath, commandsValidated: true);
                    PlatformPathResult samePathResult = PlatformPathResult.Succeeded(
                        query.StartPosition,
                        query.TargetPosition,
                        resolvedTarget,
                        samePlatformPath,
                        samePlatformPath.GetCurrentCommand());
                    PlatformRouteCost sameCost = CalculateRouteCost(samePlatformPath);
                    routeResults[queryIndex] = new PlatformRouteQueryResult(
                        true,
                        samePlatformPath.CompletionKind,
                        PlatformPathFailureReason.None,
                        sameCost,
                        resolvedTarget,
                        samePlatformPath,
                        BuildRouteDebugSummary(samePathResult, sameCost));
                    resultAssigned[queryIndex] = true;
                    continue;
                }

                Collider2D endPlatform = DetectPlatformBelow(resolvedTarget);
                PlatformNodeData? endNode = graphGenerator.FindNearestNodeOnPlatform(
                    resolvedTarget,
                    endPlatform,
                    config.MaxNodeSearchRadius);
                if (!endNode.HasValue)
                {
                    routeResults[queryIndex] = PlatformRouteQueryResult.Failed(
                        PlatformPathFailureReason.EndNodeNotFound,
                        resolvedTarget,
                        "RouteBatchEndNodeNotFound");
                    resultAssigned[queryIndex] = true;
                    continue;
                }

                bool targetIsElevated = resolvedTarget.y >
                                        startNode.Value.Position.y + config.WalkCommandVerticalTolerance;
                var costContext = new PlatformPathSearchCostContext(
                    targetIsElevated,
                    startNode.Value.Position.y,
                    config.WalkCommandVerticalTolerance);
                var key = new RouteBatchGroupKey(costContext, query.AllowPartialPath);
                if (!groups.TryGetValue(key, out RouteBatchGroup group))
                {
                    group = new RouteBatchGroup(costContext, query.AllowPartialPath);
                    groups.Add(key, group);
                }

                List<PlatformNodeData> candidates = BuildTargetNodeCandidates(endNode.Value, resolvedTarget);
                int targetStart = group.TargetNodeIds.Count;
                for (int i = 0; i < candidates.Count; i++)
                    group.TargetNodeIds.Add(candidates[i].NodeId);
                group.Items.Add(new RouteBatchItem(
                    queryIndex,
                    query,
                    resolvedTarget,
                    targetStart,
                    candidates.Count));
            }

            if (groups.Count == 0)
            {
                return PlatformRouteBatchSubmission.Immediate(CreateRouteBatchResult(
                    batchRevision,
                    snapshot,
                    routeResults));
            }

            if (!searchBackend.IsGraphReady(snapshot.GraphIdentity, snapshot.GraphRevision))
            {
                PlatformSearchGraphPreparation preparation = searchBackend.PrepareGraph(snapshot);
                if (preparation.Kind != PlatformSearchGraphPreparationKind.Ready)
                {
                    return PlatformRouteBatchSubmission.Rejected(CreateFailedRouteBatch(
                        batchRevision,
                        batch,
                        PlatformPathFailureReason.BackendUnavailable,
                        "RouteBatchBackendUnavailable"));
                }
            }

            int pendingCount = 0;
            var childHandles = new List<PlatformPathSearchHandle>(groups.Count);
            PlatformPathSearchHandle aggregateHandle = null;
            foreach (RouteBatchGroup group in groups.Values)
            {
                RouteBatchGroup callbackGroup = group;
                var request = new PlatformSearchRequest(
                    batchRevision,
                    snapshot,
                    startNode.Value.NodeId,
                    group.TargetNodeIds.ToArray(),
                    group.CostContext,
                    group.AllowPartialPath);
                PlatformSearchSubmission submission = searchBackend.Submit(
                    request,
                    searchResult =>
                    {
                        ApplyRouteBatchGroupResult(
                            snapshot,
                            startNode.Value,
                            callbackGroup,
                            searchResult,
                            routeResults,
                            resultAssigned);
                        pendingCount--;
                        if (pendingCount == 0 && aggregateHandle != null && aggregateHandle.TryComplete())
                        {
                            completed?.Invoke(CreateRouteBatchResult(
                                batchRevision,
                                snapshot,
                                routeResults));
                        }
                    });

                if (submission.Kind == PlatformSearchSubmissionKind.Pending)
                {
                    pendingCount++;
                    childHandles.Add(submission.Handle);
                }
                else
                {
                    ApplyRouteBatchGroupResult(
                        snapshot,
                        startNode.Value,
                        group,
                        submission.ImmediateResult,
                        routeResults,
                        resultAssigned);
                }
            }

            if (pendingCount == 0)
            {
                return PlatformRouteBatchSubmission.Immediate(CreateRouteBatchResult(
                    batchRevision,
                    snapshot,
                    routeResults));
            }

            aggregateHandle = new PlatformPathSearchHandle(batchRevision);
            aggregateHandle.SetCancelAction(() =>
            {
                for (int i = 0; i < childHandles.Count; i++)
                    childHandles[i]?.Cancel();
            });
            return PlatformRouteBatchSubmission.Pending(aggregateHandle);
        }

        private bool TryCreatePathResult(PlatformPathRequest request, bool mutateCurrentPath, out PlatformPathResult result)
        {
            // 检查请求间隔
            if (mutateCurrentPath && !request.ForceRequest && Time.time - lastPathRequestTime < config.PathRequestInterval)
            {
                CommitPathState(CurrentPath, PlatformPathFailureReason.Throttled, mutateCurrentPath);
                result = PlatformPathResult.Failed(
                    PlatformPathFailureReason.Throttled,
                    request.Start,
                    request.Target,
                    request.Target,
                    CurrentPath,
                    requestStarted: false);
                return false;
            }

            // 检查图是否已生成
            if (graphGenerator == null)
            {
                CommitPathState(null, PlatformPathFailureReason.MissingGraphGenerator, mutateCurrentPath);
                result = PlatformPathResult.Failed(
                    PlatformPathFailureReason.MissingGraphGenerator,
                    request.Start,
                    request.Target,
                    request.Target,
                    requestStarted: false);
                return false;
            }

            if (!graphGenerator.IsGenerated)
            {
                var notFound = Platform2DPath.NotFound(request.Start, request.Target);
                CommitPathState(notFound, PlatformPathFailureReason.GraphNotGenerated, mutateCurrentPath);
                result = PlatformPathResult.Failed(
                    PlatformPathFailureReason.GraphNotGenerated,
                    request.Start,
                    request.Target,
                    request.Target,
                    notFound,
                    requestStarted: false);
                return false;
            }

            if (mutateCurrentPath)
                lastPathRequestTime = Time.time;

            var resolvedTarget = request.Target;
            if (request.ProjectTargetToGround)
            {
                resolvedTarget = PlatformTargetProjection.ProjectTargetToGround(
                    request.Target,
                    graphGenerator.Config.AllPlatformLayers,
                    request.ProjectionDistance);
            }

            // 检查是否已经到达目标
            if (Vector2.Distance(request.Start, resolvedTarget) <= config.ArriveDistance)
            {
                var arrived = Platform2DPath.Arrived(request.Start, resolvedTarget);
                ApplyRouteDiagnostics(arrived, commandsValidated: true);
                CommitPathState(arrived, PlatformPathFailureReason.None, mutateCurrentPath);
                result = PlatformPathResult.Succeeded(
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    arrived,
                    null);
                return true;
            }

            // 尝试同平台快速路径（跳过 A*）
            var samePlatformPath = TryCreateSamePlatformPath(request.Start, resolvedTarget);
            if (samePlatformPath != null)
            {
                ApplyRouteDiagnostics(samePlatformPath, commandsValidated: true);
                CommitPathState(samePlatformPath, PlatformPathFailureReason.None, mutateCurrentPath);
                result = PlatformPathResult.Succeeded(
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    samePlatformPath,
                    samePlatformPath.GetCurrentCommand());
                return true;
            }

            // 查找起点和终点节点
            // 优先选择脚下平台上的节点，避免错误选择头顶平台节点
            var endPlatform = DetectPlatformBelow(resolvedTarget);
            var startNode = ResolveStartNode(request.Start);
            var endNode = graphGenerator.FindNearestNodeOnPlatform(resolvedTarget, endPlatform, config.MaxNodeSearchRadius);

            if (!startNode.HasValue)
            {
                var notFound = Platform2DPath.NotFound(request.Start, resolvedTarget);
                CommitPathState(notFound, PlatformPathFailureReason.StartNodeNotFound, mutateCurrentPath);
                result = PlatformPathResult.Failed(
                    PlatformPathFailureReason.StartNodeNotFound,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    notFound);
                return false;
            }

            if (!endNode.HasValue)
            {
                if (request.ShouldAllowPartialPath(config.AllowPartialPath))
                {
                    var partialPath = TryCreatePartialPath(request.Start, resolvedTarget, startNode, endNode);
                    if (partialPath != null && !IsBadElevatedPartialPath(partialPath, request.Start, resolvedTarget))
                    {
                        ApplyRouteDiagnostics(partialPath, commandsValidated: true);
                        bool recoveredFullPath = partialPath.Status == PathStatus.Valid &&
                                                 partialPath.CompletionKind == PlatformPathCompletionKind.FullPath;
                        var recoveredFailureReason = recoveredFullPath
                            ? PlatformPathFailureReason.None
                            : PlatformPathFailureReason.PartialPath;
                        CommitPathState(partialPath, recoveredFailureReason, mutateCurrentPath);
                        result = recoveredFullPath
                            ? PlatformPathResult.Succeeded(
                                request.Start,
                                request.Target,
                                partialPath.EndPosition,
                                partialPath,
                                partialPath.GetCurrentCommand())
                            : PlatformPathResult.Partial(
                                recoveredFailureReason,
                                request.Start,
                                request.Target,
                                partialPath.EndPosition,
                                partialPath,
                                partialPath.GetCurrentCommand());
                        return recoveredFullPath;
                    }
                }

                var notFound = Platform2DPath.NotFound(request.Start, resolvedTarget);
                CommitPathState(notFound, PlatformPathFailureReason.EndNodeNotFound, mutateCurrentPath);
                result = PlatformPathResult.Failed(
                    PlatformPathFailureReason.EndNodeNotFound,
                    request.Start,
                    request.Target,
                    resolvedTarget,
                    notFound);
                return false;
            }

            // 执行 A* 寻路。对高处目标同时尝试目标平台边缘节点，避免锁到平台中点后只在下方顶头。
            var path = FindBestPathToTarget(startNode.Value, endNode.Value, request.Start, resolvedTarget);

            // 如果找不到路径，尝试回退策略
            if (path.Status == PathStatus.NotFound && request.ShouldAllowPartialPath(config.AllowPartialPath))
            {
                var partialPath = TryFindPartialPath(startNode.Value, endNode.Value, request.Start, resolvedTarget);
                if (partialPath != null && !IsBadElevatedPartialPath(partialPath, request.Start, resolvedTarget))
                {
                    path = partialPath;
                }
            }

            bool partial = path.Status == PathStatus.Valid &&
                           path.CompletionKind == PlatformPathCompletionKind.Partial;
            bool success = path.Status == PathStatus.Valid && !partial;

            bool commandsValidated = false;
            PlatformPathFailureReason failureReason = success
                ? PlatformPathFailureReason.None
                : partial
                    ? PlatformPathFailureReason.PartialPath
                : request.ShouldAllowPartialPath(config.AllowPartialPath)
                    ? PlatformPathFailureReason.PartialPathUnavailable
                    : PlatformPathFailureReason.PathNotFound;
            Platform2DPath invalidPath = null;
            if (success && !ValidatePathCommands(path, out failureReason))
            {
                invalidPath = path;
                path = Platform2DPath.NotFound(request.Start, resolvedTarget);
                success = false;
                partial = false;
            }
            else
            {
                commandsValidated = success || partial;
            }

            ApplyRouteDiagnostics(path, commandsValidated);
            CommitPathState(path, failureReason, mutateCurrentPath);
            var resultResolvedTarget = (success || partial) && path != null
                ? path.EndPosition
                : resolvedTarget;
            result = success
                ? PlatformPathResult.Succeeded(request.Start, request.Target, resultResolvedTarget, path, path.GetCurrentCommand())
                : partial
                    ? PlatformPathResult.Partial(failureReason, request.Start, request.Target, resultResolvedTarget, path, path.GetCurrentCommand())
                : PlatformPathResult.Failed(failureReason, request.Start, request.Target, resolvedTarget, invalidPath ?? path);
            return success;
        }

        private void CommitPathState(Platform2DPath path, PlatformPathFailureReason failureReason, bool mutateCurrentPath)
        {
            if (!mutateCurrentPath)
                return;

            CurrentPath = path;
            LastFailureReason = failureReason;
        }

        private PlatformRouteCost CalculateRouteCost(Platform2DPath path)
        {
            if (path == null || path.Commands == null)
                return PlatformRouteCost.Unreachable;

            float horizontal = 0f;
            float vertical = 0f;
            int jumps = 0;
            int falls = 0;
            int dropDowns = 0;
            Vector3 previous = path.StartPosition;

            foreach (var command in path.Commands)
            {
                Vector3 target = command.Target;
                horizontal += Mathf.Abs(target.x - previous.x);
                vertical += Mathf.Abs(target.y - previous.y);

                switch (command.CommandType)
                {
                    case MoveCommandType.Jump:
                        jumps++;
                        break;
                    case MoveCommandType.Fall:
                        falls++;
                        break;
                    case MoveCommandType.DropDown:
                        dropDowns++;
                        break;
                }

                previous = target;
            }

            float traversalPenalty = jumps * 3f + falls * 1.5f + dropDowns * 1.5f;
            float commandPenalty = path.Commands.Count * 0.05f;
            float total = horizontal + vertical * 0.75f + traversalPenalty + commandPenalty;
            return new PlatformRouteCost(
                total,
                horizontal,
                vertical,
                jumps,
                falls,
                dropDowns,
                path.Commands.Count);
        }

        private void ApplyRouteDiagnostics(Platform2DPath path, bool commandsValidated)
        {
            if (path == null)
                return;

            path.SetRouteDiagnostics(CalculateRouteCost(path), commandsValidated);
        }

        private string BuildRouteDebugSummary(PlatformPathResult result, PlatformRouteCost cost)
        {
            string commands = BuildCommandDebug(result.Path);
            return $"Success={result.Success}, Kind={result.CompletionKind}, Failure={result.FailureReason}, Resolved={result.ResolvedTarget:F2}, Cost={cost.Total:F2}, Commands={commands}";
        }

        private bool ValidatePathCommands(Platform2DPath path, out PlatformPathFailureReason failureReason)
        {
            if (path == null || path.Commands == null)
            {
                failureReason = PlatformPathFailureReason.InvalidCommand;
                return false;
            }

            Vector3 previous = path.StartPosition;
            foreach (var command in path.Commands)
            {
                switch (command.CommandType)
                {
                    case MoveCommandType.Walk:
                        if (!CanCreateWalkCommand(previous, command.Target))
                        {
                            failureReason = PlatformPathFailureReason.InvalidCommand;
                            return false;
                        }
                        break;
                    case MoveCommandType.Fall:
                        if (!IsFallCommandExecutable(previous, command.Target, allowOneWayInterior: false))
                        {
                            failureReason = PlatformPathFailureReason.InvalidCommand;
                            return false;
                        }
                        break;
                    case MoveCommandType.DropDown:
                        if (!IsFallCommandExecutable(previous, command.Target, allowOneWayInterior: true))
                        {
                            failureReason = PlatformPathFailureReason.InvalidCommand;
                            return false;
                        }
                        break;
                }

                previous = command.Target;
            }

            failureReason = PlatformPathFailureReason.None;
            return true;
        }

        private bool IsFallCommandExecutable(Vector3 from, Vector3 to, bool allowOneWayInterior)
        {
            if (graphGenerator == null ||
                !graphGenerator.TryFindSurfaceSegmentAt(from, config.WalkCommandVerticalTolerance, out var fromSegment))
            {
                return false;
            }

            if (to.y >= from.y - config.WalkCommandVerticalTolerance)
                return false;

            float edgeTolerance = Mathf.Max(config.ArriveDistance, 0.25f);
            bool nearLeftEdge = Mathf.Abs(from.x - fromSegment.MinX) <= edgeTolerance;
            bool nearRightEdge = Mathf.Abs(from.x - fromSegment.MaxX) <= edgeTolerance;
            return nearLeftEdge || nearRightEdge || (allowOneWayInterior && fromSegment.IsOneWay);
        }

        private PlatformNodeData? ResolveStartNode(Vector3 startPosition)
        {
            if (graphGenerator == null)
                return null;

            var startPlatform = DetectPlatformBelow(startPosition);
            if (startPlatform != null)
            {
                var surfaceNode = graphGenerator.FindNearestNodeOnPlatform(
                    startPosition,
                    startPlatform,
                    config.MaxNodeSearchRadius);
                if (surfaceNode.HasValue)
                    return surfaceNode;
            }

            return graphGenerator.FindNearestNode(startPosition, config.MaxNodeSearchRadius);
        }

        /// <summary>
        /// 尝试创建同平台快速路径（直接行走，跳过 A*）
        /// </summary>
        private Platform2DPath TryCreateSamePlatformPath(Vector3 start, Vector3 end)
        {
            // 检测起点和终点脚下的平台
            var startPlatform = DetectPlatformBelow(start);
            var endPlatform = DetectPlatformBelow(end);

            if (startPlatform == null || endPlatform == null)
            {
                return null;
            }

            var startNode = graphGenerator.FindNearestNodeOnPlatform(start, startPlatform, config.MaxNodeSearchRadius);
            var endNode = graphGenerator.FindNearestNodeOnPlatform(end, endPlatform, config.MaxNodeSearchRadius);
            if (!startNode.HasValue ||
                !endNode.HasValue ||
                startNode.Value.SurfaceGroupId < 0 ||
                startNode.Value.SurfaceGroupId != endNode.Value.SurfaceGroupId)
            {
                return null;
            }

            // 检查高度差是否在允许范围内
            float heightDiff = Mathf.Abs(start.y - end.y);
            if (heightDiff > config.SamePlatformMaxHeightDiff)
            {
                return null;
            }

            // 检查中间是否有间隙（简单的射线检测）
            if (!CanWalkDirectly(start, end))
            {
                return null;
            }

            // 创建直接行走指令
            float deltaX = end.x - start.x;
            int facing = deltaX > 0.1f ? 1 : (deltaX < -0.1f ? -1 : 0);

            var commands = new List<MoveCommand>
            {
                MoveCommand.Walk(end, Vector2.Distance(start, end) / config.WalkSpeed, facing)
            };

            return new Platform2DPath(start, end, commands);
        }

        /// <summary>
        /// 检测位置下方的平台
        /// </summary>
        private Collider2D DetectPlatformBelow(Vector3 position)
        {
            // 射线长度需要足够长，覆盖角色可能站立的高度范围
            var hit = Physics2D.Raycast(
                position + Vector3.up * 0.1f,
                Vector2.down,
                config.MaxNodeSearchRadius,  // 使用节点搜索半径，确保能检测到脚下平台
                graphGenerator.Config.AllPlatformLayers
            );
            return hit.collider;
        }

        /// <summary>
        /// 检查是否可以直接行走到目标
        /// </summary>
        private bool CanWalkDirectly(Vector3 start, Vector3 end)
        {
            float distance = Vector2.Distance(start, end);
            int checkCount = Mathf.CeilToInt(distance / 0.5f);

            for (int i = 0; i <= checkCount; i++)
            {
                float t = (float)i / checkCount;
                Vector2 checkPos = Vector2.Lerp(start, end, t);

                // 向下检测地面
                var hit = Physics2D.Raycast(
                    checkPos + Vector2.up * 0.5f,
                    Vector2.down,
                    1f,
                    graphGenerator.Config.AllPlatformLayers
                );

                if (hit.collider == null)
                {
                    return false; // 有间隙
                }
            }

            return true;
        }

        /// <summary>
        /// 尝试创建部分路径（起点或终点找不到节点时的回退）
        /// </summary>
        private Platform2DPath TryCreatePartialPath(Vector3 start, Vector3 end,
            PlatformNodeData? startNode, PlatformNodeData? endNode)
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.PartialFallbackAttempts);

            // 如果只是终点找不到节点，走到最近的可达位置
            if (startNode.HasValue && !endNode.HasValue)
            {
                // 找到离终点最近的节点作为临时目标
                var nearestToEnd = graphGenerator.FindNearestNode(end, float.MaxValue);
                if (nearestToEnd.HasValue)
                {
                    var path = FindPath(startNode.Value, nearestToEnd.Value, start, nearestToEnd.Value.Position);
                    return IsPathCloseEnoughToTarget(path, end)
                        ? path
                        : ToPartialPath(path);
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试找到部分路径（A* 失败时的回退）
        /// </summary>
        private Platform2DPath TryFindPartialPath(PlatformNodeData startNode, PlatformNodeData endNode,
            Vector3 actualStart, Vector3 actualEnd)
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.PartialFallbackAttempts);

            // 找到离终点最近的可达节点
            var closestReachable = FindClosestReachableNode(startNode, endNode);

            if (closestReachable.HasValue && closestReachable.Value.NodeId != startNode.NodeId)
            {
                var path = FindPath(startNode, closestReachable.Value, actualStart, closestReachable.Value.Position);
                return IsPathCloseEnoughToTarget(path, actualEnd)
                    ? path
                    : ToPartialPath(path);
            }

            return null;
        }

        private bool IsPathCloseEnoughToTarget(Platform2DPath path, Vector3 actualEnd)
        {
            if (path == null || path.Status != PathStatus.Valid)
                return false;

            float horizontalDistance = Mathf.Abs(path.EndPosition.x - actualEnd.x);
            float verticalDistance = Mathf.Abs(path.EndPosition.y - actualEnd.y);
            float verticalTargetTolerance = config.ArriveDistance + config.WalkCommandVerticalTolerance;
            return horizontalDistance <= config.ArriveDistance &&
                   verticalDistance <= verticalTargetTolerance;
        }

        private static Platform2DPath ToPartialPath(Platform2DPath path)
        {
            if (path == null || path.Status != PathStatus.Valid)
                return path;

            return Platform2DPath.Partial(path.StartPosition, path.EndPosition, path.Commands);
        }

        private bool IsBadElevatedPartialPath(Platform2DPath path, Vector3 actualStart, Vector3 actualEnd)
        {
            if (path == null || path.Commands == null || path.Commands.Count == 0)
                return false;

            if (actualEnd.y <= actualStart.y + config.SamePlatformMaxHeightDiff)
                return false;

            bool hasJump = false;
            foreach (var command in path.Commands)
            {
                if (command.CommandType == MoveCommandType.Jump)
                {
                    hasJump = true;
                    break;
                }
            }

            if (hasJump)
                return false;

            var finalTarget = path.Commands[path.Commands.Count - 1].Target;
            return finalTarget.y < actualEnd.y - config.SamePlatformMaxHeightDiff;
        }

        private Platform2DPath FindBestPathToTarget(
            PlatformNodeData startNode,
            PlatformNodeData nearestEndNode,
            Vector3 actualStart,
            Vector3 actualEnd)
        {
            Platform2DPath bestPath = null;
            float bestScore = float.MaxValue;
            var candidates = BuildTargetNodeCandidates(nearestEndNode, actualEnd);

            foreach (var candidate in candidates)
            {
                var path = FindPath(startNode, candidate, actualStart, actualEnd);
                if (path.Status != PathStatus.Valid)
                    continue;

                if (IsInvalidElevatedPath(path, actualStart, actualEnd))
                    continue;

                if (!ValidatePathCommands(path, out _))
                    continue;

                float score = ScoreTargetPath(path, actualEnd);
                if (score < bestScore)
                {
                    bestPath = path;
                    bestScore = score;
                }
            }

            PlatformPathfindingDiagnostics.Add(
                PlatformPathfindingCounterKind.CandidateTargetsEvaluated,
                candidates.Count);
            return bestPath ?? Platform2DPath.NotFound(actualStart, actualEnd);
        }

        private List<PlatformNodeData> BuildTargetNodeCandidates(
            PlatformNodeData nearestEndNode,
            Vector3 actualEnd)
        {
            var candidates = new List<PlatformNodeData> { nearestEndNode };
            var seen = new HashSet<int> { nearestEndNode.NodeId };

            int targetSurfaceGroupId = nearestEndNode.SurfaceGroupId;
            if (targetSurfaceGroupId < 0)
                return candidates;

            foreach (var node in graphGenerator.Nodes)
            {
                if (node.SurfaceGroupId != targetSurfaceGroupId || seen.Contains(node.NodeId))
                    continue;

                bool isEdge = node.NodeType == PlatformNodeType.LeftEdge ||
                              node.NodeType == PlatformNodeType.RightEdge;
                if (!isEdge)
                    continue;

                candidates.Add(node);
                seen.Add(node.NodeId);
            }

            candidates.Sort((a, b) =>
            {
                int heightCompare = Mathf.Abs(a.Position.y - actualEnd.y)
                    .CompareTo(Mathf.Abs(b.Position.y - actualEnd.y));
                if (heightCompare != 0)
                    return heightCompare;

                return Vector2.Distance(a.Position, actualEnd)
                    .CompareTo(Vector2.Distance(b.Position, actualEnd));
            });

            return candidates;
        }

        private float ScoreTargetPath(Platform2DPath path, Vector3 actualEnd)
        {
            float score = path.TotalDuration;
            bool hasTraversal = false;
            bool hasJump = false;
            bool hasDownwardTraversal = false;

            foreach (var command in path.Commands)
            {
                if (command.CommandType == MoveCommandType.Jump)
                {
                    hasJump = true;
                    hasTraversal = true;
                }
                else if (command.CommandType == MoveCommandType.Fall ||
                         command.CommandType == MoveCommandType.DropDown)
                {
                    hasTraversal = true;
                    hasDownwardTraversal = true;
                }
            }

            if (!hasTraversal && actualEnd.y > path.StartPosition.y + config.SamePlatformMaxHeightDiff)
                score += 1000f;

            if (hasDownwardTraversal && actualEnd.y > path.StartPosition.y + config.SamePlatformMaxHeightDiff)
                score += hasJump ? 25f : 2000f;

            if (path.Commands.Count > 0)
                score += Vector2.Distance(path.Commands[path.Commands.Count - 1].Target, actualEnd);

            return score;
        }

        private bool IsInvalidElevatedPath(Platform2DPath path, Vector3 actualStart, Vector3 actualEnd)
        {
            if (path == null || path.Commands == null || path.Commands.Count == 0)
                return false;

            if (actualEnd.y <= actualStart.y + config.SamePlatformMaxHeightDiff)
                return false;

            bool hasJump = false;
            bool hasDownwardTraversal = false;
            foreach (var command in path.Commands)
            {
                if (command.CommandType == MoveCommandType.Jump)
                    hasJump = true;
                else if (command.CommandType == MoveCommandType.Fall ||
                         command.CommandType == MoveCommandType.DropDown)
                    hasDownwardTraversal = true;
            }

            if (!hasJump)
                return true;

            if (!hasDownwardTraversal)
                return false;

            int lastJumpIndex = -1;
            int lastDownIndex = -1;
            for (int i = 0; i < path.Commands.Count; i++)
            {
                var type = path.Commands[i].CommandType;
                if (type == MoveCommandType.Jump)
                    lastJumpIndex = i;
                else if (type == MoveCommandType.Fall || type == MoveCommandType.DropDown)
                    lastDownIndex = i;
            }

            return lastDownIndex > lastJumpIndex;
        }

        /// <summary>
        /// 找到从起点可达的、离终点最近的节点
        /// </summary>
        private PlatformNodeData? FindClosestReachableNode(PlatformNodeData startNode, PlatformNodeData endNode)
        {
            PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.ReachableFallbackAttempts);
            var nodes = graphGenerator.Nodes;

            // 使用 BFS 找到所有可达节点
            var reachable = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startNode.NodeId);
            reachable.Add(startNode.NodeId);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                var links = graphGenerator.GetOutgoingLinks(current);

                foreach (var link in links)
                {
                    if (!reachable.Contains(link.ToNodeId))
                    {
                        reachable.Add(link.ToNodeId);
                        queue.Enqueue(link.ToNodeId);
                    }
                }
            }

            // 在可达节点中找到离终点最近的
            PlatformNodeData? closest = null;
            float closestDist = float.MaxValue;

            foreach (int nodeId in reachable)
            {
                var node = graphGenerator.GetNode(nodeId);
                if (!node.HasValue) continue;

                float dist = Vector2.Distance(node.Value.Position, endNode.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = node;
                }
            }

            return closest;
        }

        /// <summary>
        /// 验证当前路径是否仍然有效
        /// </summary>
        /// <param name="currentPosition">当前位置</param>
        /// <param name="targetPosition">目标位置（可能已移动）</param>
        /// <returns>验证结果</returns>
        public PathValidationResult ValidatePath(Vector3 currentPosition, Vector3 targetPosition)
        {
            if (CurrentPath == null || CurrentPath.Status != PathStatus.Valid)
            {
                return PathValidationResult.NoPath;
            }

            // 检查路径是否过期
            if (CurrentPath.IsExpired(config.PathExpireTime))
            {
                return PathValidationResult.Expired;
            }

            // 检查目标是否移动过远
            float targetMoveDist = Vector2.Distance(CurrentPath.EndPosition, targetPosition);
            if (targetMoveDist > config.TargetMoveThreshold)
            {
                return PathValidationResult.TargetMoved;
            }

            // 检查是否偏离路径
            var currentCmd = CurrentPath.GetCurrentCommand();
            if (currentCmd.HasValue)
            {
                var command = currentCmd.Value;
                bool deviated = command.CommandType switch
                {
                    MoveCommandType.Walk => Vector2.Distance(currentPosition, command.Target) > config.PathDeviationThreshold,
                    MoveCommandType.Jump => Mathf.Abs(currentPosition.x - command.Target.x) > config.PathDeviationThreshold,
                    MoveCommandType.Fall => Mathf.Abs(currentPosition.x - command.Target.x) > config.PathDeviationThreshold,
                    MoveCommandType.DropDown => Mathf.Abs(currentPosition.x - command.Target.x) > config.PathDeviationThreshold,
                    _ => false
                };

                if (deviated)
                {
                    return PathValidationResult.Deviated;
                }
            }

            return PathValidationResult.Valid;
        }

        /// <summary>
        /// 自动验证并在需要时重新寻路
        /// </summary>
        /// <param name="currentPosition">当前位置</param>
        /// <param name="targetPosition">目标位置</param>
        /// <returns>如果重新寻路返回 true</returns>
        public bool TryAutoRevalidate(Vector3 currentPosition, Vector3 targetPosition)
        {
            if (!config.AutoValidatePath) return false;

            var result = ValidatePath(currentPosition, targetPosition);

            if (result != PathValidationResult.Valid && result != PathValidationResult.NoPath)
            {
                return RequestPath(currentPosition, targetPosition, forceRequest: true);
            }

            return false;
        }

        /// <summary>
        /// 获取当前移动指令
        /// </summary>
        public MoveCommand? GetCurrentCommand()
        {
            if (CurrentPath == null || CurrentPath.Status != PathStatus.Valid)
            {
                return null;
            }

            return CurrentPath.GetCurrentCommand();
        }

        /// <summary>
        /// 完成当前指令，前进到下一个
        /// </summary>
        public bool AdvanceToNextCommand()
        {
            if (CurrentPath == null) return false;
            return CurrentPath.AdvanceToNext();
        }

        /// <summary>
        /// 检查当前指令是否完成
        /// </summary>
        /// <param name="currentPosition">当前位置</param>
        /// <param name="isGrounded">是否在地面</param>
        /// <returns>是否完成</returns>
        public bool IsCurrentCommandComplete(Vector3 currentPosition, bool isGrounded)
        {
            var cmd = GetCurrentCommand();
            if (!cmd.HasValue) return true;

            var command = cmd.Value;

            switch (command.CommandType)
            {
                case MoveCommandType.Walk:
                    float horizontalDelta = command.Target.x - currentPosition.x;
                    float arriveDistance = IsNextCommandTraversal()
                        ? Mathf.Min(config.WalkCommandArriveDistance, config.TraversalApproachArriveDistance)
                        : config.WalkCommandArriveDistance;
                    bool reachedOrPassedTarget =
                        Mathf.Abs(horizontalDelta) < Mathf.Max(0f, arriveDistance) ||
                        (command.FacingDirection > 0 && horizontalDelta < 0f) ||
                        (command.FacingDirection < 0 && horizontalDelta > 0f);
                    if (!reachedOrPassedTarget)
                        return false;

                    float verticalDelta = currentPosition.y - command.Target.y;
                    if (Mathf.Abs(verticalDelta) <= config.WalkCommandVerticalTolerance)
                        return true;

                    // Non-terminal lower walk nodes may be crossed while the actor is still airborne or climbing.
                    // The final walk command, however, represents the resolved destination plane and must not complete
                    // until the actor is actually on that plane.
                    return !IsCurrentCommandTerminal() && verticalDelta > config.WalkCommandVerticalTolerance;

                case MoveCommandType.Jump:
                    if (!isGrounded) return false;
                    if (currentPosition.y < command.Target.y - config.WalkCommandVerticalTolerance)
                        return false;
                    return Mathf.Abs(currentPosition.x - command.Target.x) < config.ArriveDistance * 2f;

                case MoveCommandType.Fall:
                case MoveCommandType.DropDown:
                    // 跳跃/下落：落地且接近目标位置
                    if (!isGrounded) return false;
                    return Vector2.Distance(currentPosition, command.Target) < config.ArriveDistance * 2f;

                default:
                    return true;
            }
        }

        private bool IsCurrentCommandTerminal()
        {
            return CurrentPath == null ||
                   CurrentPath.Commands == null ||
                   CurrentPath.CurrentIndex >= CurrentPath.Commands.Count - 1;
        }

        private bool IsNextCommandTraversal()
        {
            if (CurrentPath?.Commands == null)
                return false;

            int nextIndex = CurrentPath.CurrentIndex + 1;
            if (nextIndex < 0 || nextIndex >= CurrentPath.Commands.Count)
                return false;

            MoveCommandType nextType = CurrentPath.Commands[nextIndex].CommandType;
            return nextType == MoveCommandType.Jump ||
                   nextType == MoveCommandType.Fall ||
                   nextType == MoveCommandType.DropDown;
        }

        /// <summary>
        /// 清除当前路径
        /// </summary>
        public void ClearPath()
        {
            CancelPendingPathQuery();
            CurrentPath = null;
        }

        /// <summary>
        /// 获取当前寻路诊断快照。
        /// </summary>
        public PlatformNavigationSnapshot GetSnapshot()
        {
            int currentSegmentId = ResolveSurfaceSegmentId(transform.position);
            int targetSegmentId = CurrentPath != null
                ? ResolveSurfaceSegmentId(CurrentPath.EndPosition)
                : -1;
            float routeCost = CurrentPath != null ? CalculateRouteCost(CurrentPath).Total : 0f;

            return new PlatformNavigationSnapshot(
                graphGenerator != null,
                graphGenerator != null && graphGenerator.IsGenerated,
                graphGenerator != null ? graphGenerator.Nodes.Count : 0,
                graphGenerator != null ? graphGenerator.Links.Count : 0,
                HasValidPath,
                CurrentPath != null && CurrentPath.Commands != null ? CurrentPath.Commands.Count : 0,
                CurrentPath != null ? CurrentPath.CurrentIndex : -1,
                LastFailureReason,
                CurrentPath != null ? CurrentPath.CompletionKind : PlatformPathCompletionKind.Failed,
                BuildCommandDebug(CurrentPath),
                graphGenerator != null ? graphGenerator.SurfaceSegments.Count : 0,
                graphGenerator != null ? graphGenerator.BuildSurfaceSegmentDebug() : "none",
                routeCost,
                currentSegmentId,
                targetSegmentId,
                CurrentPath != null && CurrentPath.CommandsValidated);
        }

        private int ResolveSurfaceSegmentId(Vector3 position)
        {
            if (graphGenerator == null)
                return -1;

            return graphGenerator.TryFindSurfaceSegmentAt(position, config.WalkCommandVerticalTolerance, out var segment)
                ? segment.Id
                : -1;
        }

        private string BuildCommandDebug(Platform2DPath path)
        {
            if (path?.Commands == null || path.Commands.Count == 0)
                return "none";

            var parts = new List<string>(path.Commands.Count);
            for (int i = 0; i < path.Commands.Count; i++)
            {
                var command = path.Commands[i];
                int groupId = ResolveCommandTargetSurfaceGroup(command);
                string currentMarker = i == path.CurrentIndex ? "*" : "";
                parts.Add($"{currentMarker}{i}:{command.CommandType}@{command.Target:F2}/face={command.FacingDirection}/group={groupId}");
            }

            return string.Join(" -> ", parts);
        }

        private int ResolveCommandTargetSurfaceGroup(MoveCommand command)
        {
            if (graphGenerator == null)
                return -1;

            var node = graphGenerator.FindNearestNode(command.Target, config.MaxNodeSearchRadius);
            return node.HasValue ? node.Value.SurfaceGroupId : -1;
        }

        /// <summary>
        /// A* 寻路实现
        /// </summary>
        private Platform2DPath FindPath(PlatformNodeData startNode, PlatformNodeData endNode, Vector3 actualStart, Vector3 actualEnd)
        {
            bool captureDiagnostics = PlatformPathfindingDiagnostics.IsCapturing;
            if (captureDiagnostics)
                PlatformPathfindingDiagnostics.Increment(PlatformPathfindingCounterKind.FindPathCalls);

            long expandedNodes = captureDiagnostics ? 0 : -1;
            long openSetPeak = captureDiagnostics ? 1 : 0;
            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.FindPath))
            {
                try
                {
                    return FindPathCore(
                        startNode,
                        endNode,
                        actualStart,
                        actualEnd,
                        captureDiagnostics,
                        ref expandedNodes,
                        ref openSetPeak);
                }
                finally
                {
                    if (captureDiagnostics)
                    {
                        PlatformPathfindingDiagnostics.Add(
                            PlatformPathfindingCounterKind.ExpandedNodes,
                            expandedNodes);
                        PlatformPathfindingDiagnostics.RecordMaximum(
                            PlatformPathfindingCounterKind.OpenSetPeak,
                            openSetPeak);
                    }
                }
            }
        }

        private Platform2DPath FindPathCore(
            PlatformNodeData startNode,
            PlatformNodeData endNode,
            Vector3 actualStart,
            Vector3 actualEnd,
            bool captureDiagnostics,
            ref long expandedNodes,
            ref long openSetPeak)
        {
            var nodes = graphGenerator.Nodes;
            var nodeCount = nodes.Count;
            bool targetIsElevated = actualEnd.y > startNode.Position.y + config.WalkCommandVerticalTolerance;

            // 初始化寻路数据
            var gScore = new Dictionary<int, float>();
            var fScore = new Dictionary<int, float>();
            var cameFrom = new Dictionary<int, int>();
            var cameFromLink = new Dictionary<int, PlatformLinkData>();

            var openSet = new List<int>();
            var closedSet = new HashSet<int>();

            // 初始化起点
            gScore[startNode.NodeId] = 0;
            fScore[startNode.NodeId] = Heuristic(startNode.Position, endNode.Position);
            openSet.Add(startNode.NodeId);

            while (openSet.Count > 0)
            {
                // 找到 fScore 最小的节点
                int current = GetLowestFScore(openSet, fScore);

                // 到达终点
                if (current == endNode.NodeId)
                {
                    if (captureDiagnostics)
                        expandedNodes = closedSet.Count + 1;
                    return ReconstructPath(cameFrom, cameFromLink, current, actualStart, actualEnd);
                }

                openSet.Remove(current);
                closedSet.Add(current);

                // 遍历所有出边
                var outgoingLinks = graphGenerator.GetOutgoingLinks(current);

                foreach (var link in outgoingLinks)
                {
                    if (closedSet.Contains(link.ToNodeId)) continue;

                    var toNode = graphGenerator.GetNode(link.ToNodeId);
                    if (!toNode.HasValue)
                        continue;

                    float tentativeG = gScore[current] + link.Cost +
                                       GetElevatedRouteLowNodePenalty(targetIsElevated, startNode.Position.y, toNode.Value);

                    if (!gScore.ContainsKey(link.ToNodeId) || tentativeG < gScore[link.ToNodeId])
                    {
                        cameFrom[link.ToNodeId] = current;
                        cameFromLink[link.ToNodeId] = link;
                        gScore[link.ToNodeId] = tentativeG;

                        fScore[link.ToNodeId] = tentativeG + Heuristic(toNode.Value.Position, endNode.Position);

                        if (!openSet.Contains(link.ToNodeId))
                        {
                            openSet.Add(link.ToNodeId);
                            if (captureDiagnostics && openSet.Count > openSetPeak)
                                openSetPeak = openSet.Count;
                        }
                    }
                }
            }

            // 找不到路径
            if (captureDiagnostics)
                expandedNodes = closedSet.Count;
            return Platform2DPath.NotFound(actualStart, actualEnd);
        }

        private float GetElevatedRouteLowNodePenalty(bool targetIsElevated, float startSurfaceY, PlatformNodeData toNode)
        {
            if (!targetIsElevated)
                return 0f;

            return toNode.Position.y < startSurfaceY - config.WalkCommandVerticalTolerance
                ? 1000f
                : 0f;
        }

        /// <summary>
        /// 启发式函数（曼哈顿距离 + 高度惩罚）
        /// </summary>
        private float Heuristic(Vector3 a, Vector3 b)
        {
            float dx = Mathf.Abs(a.x - b.x);
            float dy = Mathf.Abs(a.y - b.y);

            // 向上移动代价更高
            if (b.y > a.y)
            {
                dy *= 1.5f;
            }

            return dx + dy;
        }

        /// <summary>
        /// 获取 fScore 最小的节点
        /// </summary>
        private int GetLowestFScore(List<int> openSet, Dictionary<int, float> fScore)
        {
            int best = openSet[0];
            float bestScore = fScore.ContainsKey(best) ? fScore[best] : float.MaxValue;

            for (int i = 1; i < openSet.Count; i++)
            {
                int node = openSet[i];
                float score = fScore.ContainsKey(node) ? fScore[node] : float.MaxValue;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = node;
                }
            }

            return best;
        }

        /// <summary>
        /// 重建路径并生成 MoveCommand
        /// </summary>
        private Platform2DPath ReconstructPath(
            Dictionary<int, int> cameFrom,
            Dictionary<int, PlatformLinkData> cameFromLink,
            int current,
            Vector3 actualStart,
            Vector3 actualEnd)
        {
            var nodePath = new List<int>();
            var linkPath = new List<PlatformLinkData>();

            // 回溯路径
            while (cameFrom.ContainsKey(current))
            {
                nodePath.Insert(0, current);
                if (cameFromLink.ContainsKey(current))
                {
                    linkPath.Insert(0, cameFromLink[current]);
                }
                current = cameFrom[current];
            }
            nodePath.Insert(0, current); // 添加起点

            // 转换为 MoveCommand
            var commands = new List<MoveCommand>();
            Vector3 commandStart = actualStart;
            Vector3 generatedEnd = actualStart;

            // 从实际起点走到第一个节点
            if (nodePath.Count > 0)
            {
                var firstNode = graphGenerator.GetNode(nodePath[0]);
                if (firstNode.HasValue)
                {
                    float dist = Vector2.Distance(actualStart, firstNode.Value.Position);
                    generatedEnd = firstNode.Value.Position;
                    bool firstLinkIsTraversal = linkPath.Count > 0 &&
                                                (linkPath[0].LinkType == PlatformLinkType.Jump ||
                                                 linkPath[0].LinkType == PlatformLinkType.Fall ||
                                                 linkPath[0].LinkType == PlatformLinkType.DropThrough);
                    float initialWalkArriveDistance = firstLinkIsTraversal
                        ? Mathf.Min(config.WalkCommandArriveDistance, config.TraversalApproachArriveDistance)
                        : config.WalkCommandArriveDistance;
                    if (dist > initialWalkArriveDistance &&
                        CanCreateWalkCommand(actualStart, firstNode.Value.Position) &&
                        !IsShortInitialBacktrack(actualStart, firstNode.Value.Position, dist, linkPath))
                    {
                        // 计算朝向：根据目标 X 与起点 X 的差值
                        float deltaX = firstNode.Value.Position.x - actualStart.x;
                        int facing = CalculateFacingDirection(deltaX);

                        commands.Add(MoveCommand.Walk(
                            firstNode.Value.Position,
                            dist / config.WalkSpeed,
                            facing
                        ));
                    }
                    else
                    {
                        commandStart = firstNode.Value.Position;
                    }
                }
            }

            // 处理路径中的每个链接
            Vector3 prevPosition = nodePath.Count > 0 ? graphGenerator.GetNode(nodePath[0])?.Position ?? actualStart : actualStart;

            foreach (var link in linkPath)
            {
                var fromNode = graphGenerator.GetNode(link.FromNodeId);
                var toNode = graphGenerator.GetNode(link.ToNodeId);
                if (!toNode.HasValue) continue;

                // 计算朝向：根据目标位置与前一个位置的 X 差值
                float deltaX = toNode.Value.Position.x - prevPosition.x;
                int facing = CalculateFacingDirection(deltaX);

                switch (link.LinkType)
                {
                    case PlatformLinkType.Walk:
                        if (!CanCreateWalkCommand(prevPosition, toNode.Value.Position))
                            return Platform2DPath.NotFound(actualStart, actualEnd);

                        commands.Add(MoveCommand.Walk(toNode.Value.Position, link.Duration, facing));
                        break;

                    case PlatformLinkType.Jump:
                        if (link.FacingDirectionOverride != 0)
                            facing = link.FacingDirectionOverride > 0 ? 1 : -1;

                        commands.Add(MoveCommand.Jump(
                            toNode.Value.Position,
                            link.JumpVelocityY,
                            link.JumpVelocityX,
                            link.Duration,
                            link.JumpTrajectory,  // 传递预计算的轨迹点用于可视化
                            facing,
                            link.HasWallContactAnchor,
                            link.WallContactX
                        ));
                        break;

                    case PlatformLinkType.Fall:
                        if (facing == 0 && fromNode.HasValue)
                            facing = ResolveEdgeExitFacing(fromNode.Value);
                        commands.Add(MoveCommand.Fall(toNode.Value.Position, link.Duration, facing));
                        break;

                    case PlatformLinkType.DropThrough:
                        if (facing == 0 && fromNode.HasValue)
                            facing = ResolveEdgeExitFacing(fromNode.Value);
                        commands.Add(MoveCommand.DropDown(
                            toNode.Value.Position,
                            toNode.Value.PlatformCollider,
                            link.Duration,
                            facing
                        ));
                        break;
                }

                prevPosition = toNode.Value.Position;
                generatedEnd = toNode.Value.Position;
            }

            // 从最后一个节点走到实际终点
            if (nodePath.Count > 0)
            {
                var lastNode = graphGenerator.GetNode(nodePath[nodePath.Count - 1]);
                if (lastNode.HasValue)
                {
                    float dist = Vector2.Distance(lastNode.Value.Position, actualEnd);
                    if (dist > config.WalkCommandArriveDistance && CanCreateWalkCommand(lastNode.Value.Position, actualEnd))
                    {
                        // 计算朝向
                        float deltaX = actualEnd.x - lastNode.Value.Position.x;
                        int facing = CalculateFacingDirection(deltaX);

                        commands.Add(MoveCommand.Walk(
                            actualEnd,
                            dist / config.WalkSpeed,
                            facing
                        ));
                        generatedEnd = actualEnd;
                    }
                }
            }

            return new Platform2DPath(commandStart, generatedEnd, commands);
        }

        private bool IsShortInitialBacktrack(
            Vector3 actualStart,
            Vector3 firstNodePosition,
            float distanceToFirstNode,
            List<PlatformLinkData> linkPath)
        {
            if (distanceToFirstNode > config.ArriveDistance ||
                linkPath == null ||
                linkPath.Count == 0 ||
                graphGenerator == null)
            {
                return false;
            }

            var nextNode = graphGenerator.GetNode(linkPath[0].ToNodeId);
            if (!nextNode.HasValue)
                return false;

            const float directionEpsilon = 0.05f;
            float initialDx = firstNodePosition.x - actualStart.x;
            float routeDx = nextNode.Value.Position.x - firstNodePosition.x;

            return Mathf.Abs(initialDx) > directionEpsilon &&
                   Mathf.Abs(routeDx) > directionEpsilon &&
                   Mathf.Sign(initialDx) != Mathf.Sign(routeDx);
        }

        private int ResolveEdgeExitFacing(PlatformNodeData node)
        {
            if (node.NodeType == PlatformNodeType.LeftEdge)
                return -1;
            if (node.NodeType == PlatformNodeType.RightEdge)
                return 1;
            return 0;
        }

        private bool CanCreateWalkCommand(Vector3 from, Vector3 to)
        {
            return Mathf.Abs(from.y - to.y) <= config.WalkCommandVerticalTolerance;
        }

        /// <summary>
        /// 根据 X 轴差值计算朝向
        /// </summary>
        /// <param name="deltaX">目标 X - 当前 X</param>
        /// <returns>-1=左, 0=保持, 1=右</returns>
        private int CalculateFacingDirection(float deltaX)
        {
            const float threshold = 0.1f;
            if (deltaX > threshold) return 1;   // 向右
            if (deltaX < -threshold) return -1; // 向左
            return 0; // 保持当前朝向
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (CurrentPath == null || CurrentPath.Status != PathStatus.Valid) return;

            // 绘制当前路径
            Gizmos.color = Color.magenta;

            for (int i = CurrentPath.CurrentIndex; i < CurrentPath.Commands.Count; i++)
            {
                var cmd = CurrentPath.Commands[i];

                // 绘制目标点
                switch (cmd.CommandType)
                {
                    case MoveCommandType.Walk:
                        Gizmos.color = Color.green;
                        break;
                    case MoveCommandType.Jump:
                        Gizmos.color = Color.yellow;
                        break;
                    case MoveCommandType.Fall:
                    case MoveCommandType.DropDown:
                        Gizmos.color = Color.blue;
                        break;
                }

                Gizmos.DrawWireSphere(cmd.Target, 0.3f);

                // 绘制连线
                if (i > CurrentPath.CurrentIndex)
                {
                    var prevCmd = CurrentPath.Commands[i - 1];
                    Gizmos.DrawLine(prevCmd.Target, cmd.Target);
                }
            }
        }
#endif
    }
}
