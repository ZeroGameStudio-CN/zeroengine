// PlatformGraphGenerator.cs
// 平台图生成器
// 扫描场景中的平台碰撞体，生成用于寻路的节点网络

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Tilemaps;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 平台图生成配置
    /// </summary>
    [System.Serializable]
    public class PlatformGraphConfig
    {
        [Header("扫描范围")]
        [Tooltip("扫描区域中心")]
        public Vector2 ScanCenter = Vector2.zero;

        [Tooltip("扫描区域尺寸")]
        public Vector2 ScanSize = new Vector2(100f, 50f);

        [Header("节点生成")]
        [Tooltip("平台表面节点间距")]
        public float NodeSpacing = 1.5f;

        [Tooltip("边缘节点内缩距离")]
        public float EdgeInset = 0.3f;

        [Tooltip("最小平台宽度（小于此值的平台只生成中心节点）")]
        public float MinPlatformWidth = 1f;

        [Header("密集节点模式")]
        [Tooltip("启用密集节点生成（用于更精确的寻路）")]
        public bool UseDenseNodes = false;

        [Tooltip("密集模式节点间距")]
        public float DenseNodeSpacing = 0.75f;

        [Header("空间索引")]
        [Tooltip("空间网格单元尺寸（用于加速节点查询）")]
        public float SpatialGridCellSize = 3f;

        [Header("层级配置")]
        [Tooltip("地面层")]
        public LayerMask GroundLayer;

        [Tooltip("单向平台层")]
        public LayerMask OneWayPlatformLayer;

        [Tooltip("障碍物层（用于碰撞检测）")]
        public LayerMask ObstacleLayer;

        [Header("角色参数")]
        [Tooltip("角色碰撞体半径")]
        public float CharacterRadius = 0.4f;

        [Tooltip("角色高度")]
        public float CharacterHeight = 1.8f;

        /// <summary>
        /// 所有平台层的组合
        /// </summary>
        public LayerMask AllPlatformLayers => GroundLayer | OneWayPlatformLayer;

        /// <summary>
        /// 获取实际使用的节点间距
        /// </summary>
        public float ActualNodeSpacing => UseDenseNodes ? DenseNodeSpacing : NodeSpacing;
    }

    /// <summary>
    /// 平台图生成器
    /// 扫描场景中的 Collider2D 并生成平台节点网络
    /// </summary>
    public class PlatformGraphGenerator : MonoBehaviour
    {
        private const int SearchCostPolicyRevision = PlatformPathSearchCostContext.CurrentPolicyRevision;
        private static long nextGraphIdentity;

        [SerializeField]
        private PlatformGraphConfig config = new PlatformGraphConfig();

        /// <summary>配置</summary>
        public PlatformGraphConfig Config => config;

        /// <summary>生成的节点列表</summary>
        public List<PlatformNodeData> Nodes { get; private set; } = new List<PlatformNodeData>();

        /// <summary>节点 ID 到索引的映射</summary>
        public Dictionary<int, int> NodeIdToIndex { get; private set; } = new Dictionary<int, int>();

        /// <summary>生成的链接列表</summary>
        public List<PlatformLinkData> Links { get; private set; } = new List<PlatformLinkData>();

        /// <summary>连续可站立平台段。一个 Collider 可以包含多个平台段。</summary>
        public List<PlatformSurfaceSegment> SurfaceSegments { get; private set; } = new List<PlatformSurfaceSegment>();

        /// <summary>邻接表：节点ID -> 出边链接列表（性能优化）</summary>
        public Dictionary<int, List<PlatformLinkData>> AdjacencyList { get; private set; } = new Dictionary<int, List<PlatformLinkData>>();

        /// <summary>是否已生成</summary>
        public bool IsGenerated { get; private set; }

        /// <summary>上次生成时间</summary>
        public float LastGenerateTime { get; private set; }

        /// <summary>空间索引</summary>
        public SpatialGrid2D SpatialGrid { get; private set; }

        /// <summary>当前生成器实例的运行时搜索图身份。</summary>
        public long GraphIdentity
        {
            get
            {
                EnsureGraphIdentity();
                return graphIdentity;
            }
        }

        /// <summary>最近一次成功提交的不可变搜索图修订号。</summary>
        public long GraphRevision => graphRevision;

        /// <summary>最近一次成功提交的不可变搜索快照。</summary>
        public PlatformSearchGraphSnapshot SearchSnapshot => committedSearchSnapshot;

        public bool IsBuildInProgress => buildInProgress;

        public bool HasCommittedSearchSnapshot => committedSearchSnapshot != null;

        private int nextNodeId = 0;
        private int nextSurfaceGroupId = 0;
        private long graphIdentity;
        private long graphRevision;
        private bool buildInProgress;
        private PlatformSearchGraphSnapshot committedSearchSnapshot;
        private PlatformLinkData[] committedLinkBindings = Array.Empty<PlatformLinkData>();
        private readonly Dictionary<int, PlatformSurfaceSegment> surfaceSegmentsById = new Dictionary<int, PlatformSurfaceSegment>();

        // 缓存所有边缘数据，用于全局转换节点生成
        private readonly List<(float left, float right, float y, Collider2D collider, bool isOneWay, int surfaceGroupId)> _allEdgesCache
            = new List<(float, float, float, Collider2D, bool, int)>();

        private const float HeightTransitionMinimumDifference = 0.5f;
        private const float HeightTransitionNodeDedupTolerance = 0.3f;
        private const float HeightTransitionNodeBucketSize = HeightTransitionNodeDedupTolerance;
        private const float HeightTransitionSafeLandingContactTolerance = 0.1f;
        private const float HeightTransitionIndexCoordinateEpsilon = 0.00001f;
        private const int HeightTransitionSurfaceSide = 0;
        private const int HeightTransitionLeftSide = 1;
        private const int HeightTransitionRightSide = 2;
        private const float SurfaceNodeDedupTolerance = 0.05f;
        private const float SurfaceNodeBucketSize = SurfaceNodeDedupTolerance;

        private int heightTransitionCandidateChecks;
        private int heightTransitionIntervalQueryCount;

        /// <summary>
        /// Number of interval hits examined by the most recent graph build.
        /// This is a deterministic performance seam; it is not a gameplay limit.
        /// </summary>
        public int HeightTransitionCandidateChecks => heightTransitionCandidateChecks;

        /// <summary>
        /// Number of X-point interval queries issued by the most recent graph build.
        /// </summary>
        public int HeightTransitionIntervalQueryCount => heightTransitionIntervalQueryCount;

        private readonly Dictionary<HeightTransitionNodeBucketKey, List<float>> _heightTransitionNodeBuckets
            = new Dictionary<HeightTransitionNodeBucketKey, List<float>>();
        private bool heightTransitionNodeBucketsBuilt;

        private readonly Dictionary<SurfaceNodeBucketKey, List<Vector2>> _surfaceNodeBuckets
            = new Dictionary<SurfaceNodeBucketKey, List<Vector2>>();
        private bool surfaceNodeBucketsBuilt;

        private readonly List<int> _heightTransitionCandidatesCache = new List<int>(32);

        private readonly struct SurfaceNodeBucketKey : IEquatable<SurfaceNodeBucketKey>
        {
            private readonly int surfaceGroupId;
            private readonly int xBucket;
            private readonly int yBucket;

            public SurfaceNodeBucketKey(int surfaceGroupId, int xBucket, int yBucket)
            {
                this.surfaceGroupId = surfaceGroupId;
                this.xBucket = xBucket;
                this.yBucket = yBucket;
            }

            public bool Equals(SurfaceNodeBucketKey other)
            {
                return surfaceGroupId == other.surfaceGroupId &&
                       xBucket == other.xBucket &&
                       yBucket == other.yBucket;
            }

            public override bool Equals(object obj)
            {
                return obj is SurfaceNodeBucketKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = surfaceGroupId;
                    hash = (hash * 397) ^ xBucket;
                    return (hash * 397) ^ yBucket;
                }
            }
        }

        private struct HeightTransitionEdge
        {
            public float Left;
            public float Right;
            public float Y;
            public Collider2D Collider;
            public bool IsOneWay;
            public int SurfaceGroupId;

            public HeightTransitionEdge(
                float left,
                float right,
                float y,
                Collider2D collider,
                bool isOneWay,
                int surfaceGroupId)
            {
                Left = left;
                Right = right;
                Y = y;
                Collider = collider;
                IsOneWay = isOneWay;
                SurfaceGroupId = surfaceGroupId;
            }
        }

        private readonly struct HeightTransitionNodeBucketKey : IEquatable<HeightTransitionNodeBucketKey>
        {
            private readonly int surfaceGroupId;
            private readonly int side;
            private readonly int xBucket;

            public HeightTransitionNodeBucketKey(int surfaceGroupId, int side, int xBucket)
            {
                this.surfaceGroupId = surfaceGroupId;
                this.side = side;
                this.xBucket = xBucket;
            }

            public bool Equals(HeightTransitionNodeBucketKey other)
            {
                return surfaceGroupId == other.surfaceGroupId &&
                       side == other.side &&
                       xBucket == other.xBucket;
            }

            public override bool Equals(object obj)
            {
                return obj is HeightTransitionNodeBucketKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = surfaceGroupId;
                    hash = (hash * 397) ^ side;
                    return (hash * 397) ^ xBucket;
                }
            }
        }

        /// <summary>
        /// Static X-coordinate interval index used while a Y sweep activates edges.
        /// Intervals are decomposed into a segment tree over all query coordinates,
        /// so a point query visits O(log m + matches) entries without scanning all
        /// active intervals.
        /// </summary>
        private sealed class HeightTransitionIntervalIndex
        {
            private readonly float[] queryCoordinates;
            private readonly List<int>[] segmentTree;

            public HeightTransitionIntervalIndex(List<float> queryPoints)
            {
                if (queryPoints == null || queryPoints.Count == 0)
                {
                    queryCoordinates = Array.Empty<float>();
                    segmentTree = Array.Empty<List<int>>();
                    return;
                }

                var sorted = new List<float>(queryPoints);
                sorted.Sort();
                var unique = new List<float>(sorted.Count);
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (i == 0 || sorted[i] != sorted[i - 1])
                        unique.Add(sorted[i]);
                }

                queryCoordinates = unique.ToArray();
                segmentTree = new List<int>[queryCoordinates.Length * 4];
            }

            public void AddInterval(int edgeIndex, float left, float right)
            {
                if (queryCoordinates.Length == 0 ||
                    float.IsNaN(left) || float.IsNaN(right) ||
                    left >= right)
                {
                    return;
                }

                // The old transition tests use strict X bounds. Convert the
                // continuous interval to the query coordinates that satisfy
                // left < x < right.
                int first = UpperBound(queryCoordinates, left);
                int last = LowerBound(queryCoordinates, right) - 1;
                if (first > last)
                    return;

                AddRange(1, 0, queryCoordinates.Length - 1, first, last, edgeIndex);
            }

            public void Query(float x, List<int> results)
            {
                if (queryCoordinates.Length == 0 || results == null)
                    return;

                int index = FindCoordinate(x);
                if (index < 0)
                    return;

                QueryPoint(1, 0, queryCoordinates.Length - 1, index, results);
            }

            private void AddRange(
                int treeIndex,
                int rangeStart,
                int rangeEnd,
                int intervalStart,
                int intervalEnd,
                int edgeIndex)
            {
                if (intervalStart <= rangeStart && rangeEnd <= intervalEnd)
                {
                    List<int> entries = segmentTree[treeIndex];
                    if (entries == null)
                    {
                        entries = new List<int>();
                        segmentTree[treeIndex] = entries;
                    }

                    entries.Add(edgeIndex);
                    return;
                }

                int midpoint = rangeStart + ((rangeEnd - rangeStart) / 2);
                if (intervalStart <= midpoint)
                {
                    AddRange(
                        treeIndex * 2,
                        rangeStart,
                        midpoint,
                        intervalStart,
                        intervalEnd,
                        edgeIndex);
                }

                if (intervalEnd > midpoint)
                {
                    AddRange(
                        treeIndex * 2 + 1,
                        midpoint + 1,
                        rangeEnd,
                        intervalStart,
                        intervalEnd,
                        edgeIndex);
                }
            }

            private void QueryPoint(
                int treeIndex,
                int rangeStart,
                int rangeEnd,
                int queryIndex,
                List<int> results)
            {
                List<int> entries = segmentTree[treeIndex];
                if (entries != null)
                    results.AddRange(entries);

                if (rangeStart == rangeEnd)
                    return;

                int midpoint = rangeStart + ((rangeEnd - rangeStart) / 2);
                if (queryIndex <= midpoint)
                {
                    QueryPoint(treeIndex * 2, rangeStart, midpoint, queryIndex, results);
                }
                else
                {
                    QueryPoint(treeIndex * 2 + 1, midpoint + 1, rangeEnd, queryIndex, results);
                }
            }

            private int FindCoordinate(float value)
            {
                int index = LowerBound(queryCoordinates, value);
                if (index < queryCoordinates.Length && queryCoordinates[index] == value)
                    return index;

                if (index > 0 &&
                    Mathf.Abs(queryCoordinates[index - 1] - value) <= HeightTransitionIndexCoordinateEpsilon)
                {
                    return index - 1;
                }

                if (index < queryCoordinates.Length &&
                    Mathf.Abs(queryCoordinates[index] - value) <= HeightTransitionIndexCoordinateEpsilon)
                {
                    return index;
                }

                return -1;
            }

            private static int LowerBound(float[] values, float value)
            {
                int start = 0;
                int end = values.Length;
                while (start < end)
                {
                    int midpoint = start + ((end - start) / 2);
                    if (values[midpoint] < value)
                        start = midpoint + 1;
                    else
                        end = midpoint;
                }

                return start;
            }

            private static int UpperBound(float[] values, float value)
            {
                int start = 0;
                int end = values.Length;
                while (start < end)
                {
                    int midpoint = start + ((end - start) / 2);
                    if (values[midpoint] <= value)
                        start = midpoint + 1;
                    else
                        end = midpoint;
                }

                return start;
            }
        }

        // 复用 List 避免 GC
        private readonly List<Vector2> _pathPointsCache = new List<Vector2>(64);
        private readonly List<PlatformNodeData> _nodesInRangeCache = new List<PlatformNodeData>(32);

        /// <summary>
        /// 生成平台图
        /// </summary>
        public void GeneratePlatformGraph()
        {
            bool ownsTransaction = !buildInProgress;
            if (ownsTransaction)
                BeginBuild();

            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.GraphBuild))
            {
                try
                {
                    GeneratePlatformGraphCore();
                    if (ownsTransaction && !TryCommitBuild(out _, out string error))
                        throw new InvalidOperationException(error);
                }
                catch
                {
                    if (ownsTransaction && buildInProgress)
                        CancelBuild();
                    throw;
                }
                finally
                {
                    PlatformPathfindingDiagnostics.Add(
                        PlatformPathfindingCounterKind.GraphNodes,
                        Nodes.Count);
                    PlatformPathfindingDiagnostics.Add(
                        PlatformPathfindingCounterKind.GraphLinks,
                        Links.Count);
                }
            }
        }

        /// <summary>
        /// Reuses another compatible generator's platform scan and base walk topology.
        /// Profile-specific jump/fall/drop links are intentionally excluded and must be generated afterwards.
        /// </summary>
        public void GeneratePlatformGraphFromBase(PlatformGraphGenerator source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (!source.IsGenerated)
                throw new InvalidOperationException("The source platform graph has not been generated.");
            if (source.Config.GroundLayer.value != config.GroundLayer.value ||
                source.Config.OneWayPlatformLayer.value != config.OneWayPlatformLayer.value ||
                source.Config.ScanCenter != config.ScanCenter ||
                source.Config.ScanSize != config.ScanSize)
            {
                throw new InvalidOperationException(
                    "Base platform graphs can only be shared by profiles with the same scan and platform layers.");
            }

            bool ownsTransaction = !buildInProgress;
            if (ownsTransaction)
                BeginBuild();

            using (PlatformPathfindingDiagnostics.Measure(PlatformPathfindingMetricKind.GraphBuild))
            {
                try
                {
                    CopyBaseGraphCore(source);
                    if (ownsTransaction && !TryCommitBuild(out _, out string error))
                        throw new InvalidOperationException(error);
                }
                catch
                {
                    if (ownsTransaction && buildInProgress)
                        CancelBuild();
                    throw;
                }
                finally
                {
                    PlatformPathfindingDiagnostics.Add(
                        PlatformPathfindingCounterKind.GraphNodes,
                        Nodes.Count);
                    PlatformPathfindingDiagnostics.Add(
                        PlatformPathfindingCounterKind.GraphLinks,
                        Links.Count);
                }
            }
        }

        /// <summary>
        /// Starts an atomic search-snapshot build. Mutable legacy collections remain available,
        /// while search backends continue to observe the previously committed snapshot.
        /// </summary>
        public void BeginBuild()
        {
            if (buildInProgress)
                throw new InvalidOperationException("A platform graph build transaction is already active.");

            EnsureGraphIdentity();
            buildInProgress = true;
        }

        /// <summary>
        /// Commits the current mutable graph as one immutable search snapshot and advances one revision.
        /// </summary>
        public bool TryCommitBuild(out PlatformSearchGraphSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = null;
            if (!buildInProgress)
            {
                error = "No platform graph build transaction is active.";
                return false;
            }

            if (!IsGenerated)
            {
                error = "The mutable platform graph has not completed generation.";
                return false;
            }

            BuildAdjacencyList();
            long nextRevision = graphRevision + 1;
            if (!PlatformSearchGraphSnapshot.TryCreate(
                    GraphIdentity,
                    nextRevision,
                    SearchCostPolicyRevision,
                    Nodes,
                    Links,
                    out snapshot,
                    out error))
            {
                return false;
            }

            committedLinkBindings = Links.ToArray();
            committedSearchSnapshot = snapshot;
            graphRevision = nextRevision;
            buildInProgress = false;
            return true;
        }

        public PlatformSearchGraphSnapshot CommitBuild()
        {
            if (!TryCommitBuild(out PlatformSearchGraphSnapshot snapshot, out string error))
                throw new InvalidOperationException(error);

            return snapshot;
        }

        /// <summary>
        /// Ends a failed build without publishing partially generated data to search backends.
        /// The legacy mutable collections are not rolled back.
        /// </summary>
        public void CancelBuild()
        {
            buildInProgress = false;
        }

        public bool TryGetCommittedLink(
            long expectedGraphRevision,
            int linkId,
            out PlatformLinkData link)
        {
            if (committedSearchSnapshot != null &&
                graphRevision == expectedGraphRevision &&
                (uint)linkId < (uint)committedLinkBindings.Length)
            {
                link = committedLinkBindings[linkId];
                return true;
            }

            link = default;
            return false;
        }

        private void GeneratePlatformGraphCore()
        {
            ClearGraph();
            _allEdgesCache.Clear();  // 清空边缘缓存

            // 扫描区域内的所有平台碰撞体
            var colliders = ScanPlatformColliders();

            // 为每个平台生成节点
            foreach (var collider in colliders)
            {
                GenerateNodesForPlatform(collider);
            }

            // 全局高度转换节点生成（跨 Collider）
            GenerateGlobalHeightTransitionNodes();

            // 生成同平台行走链接
            GenerateWalkLinks();

            // 构建空间索引
            SpatialGrid = new SpatialGrid2D(config.SpatialGridCellSize);
            SpatialGrid.Build(Nodes);

            IsGenerated = true;
            LastGenerateTime = Time.time;

            if (PathfindingLogSettings.EnableGenerationSummary)
                Debug.Log($"[PlatformGraphGenerator] 生成完成: {Nodes.Count} 节点, {Links.Count} 链接, 空间索引: {SpatialGrid.GetDebugInfo()}");
        }

        private void CopyBaseGraphCore(PlatformGraphGenerator source)
        {
            ClearGraph();
            _allEdgesCache.Clear();

            for (int i = 0; i < source.Nodes.Count; i++)
            {
                PlatformNodeData node = source.Nodes[i];
                Nodes.Add(node);
                NodeIdToIndex.Add(node.NodeId, i);
            }

            for (int i = 0; i < source.Links.Count; i++)
            {
                PlatformLinkData link = source.Links[i];
                if (link.LinkType == PlatformLinkType.Walk)
                    Links.Add(link);
            }

            for (int i = 0; i < source.SurfaceSegments.Count; i++)
            {
                PlatformSurfaceSegment original = source.SurfaceSegments[i];
                var clone = new PlatformSurfaceSegment
                {
                    GroupId = original.GroupId,
                    Collider = original.Collider,
                    Left = original.Left,
                    Right = original.Right,
                    Y = original.Y,
                    IsOneWay = original.IsOneWay,
                    LeftNodeId = original.LeftNodeId,
                    RightNodeId = original.RightNodeId
                };
                clone.NodeIds.AddRange(original.NodeIds);
                SurfaceSegments.Add(clone);
                surfaceSegmentsById.Add(clone.GroupId, clone);
            }

            nextNodeId = source.nextNodeId;
            nextSurfaceGroupId = source.nextSurfaceGroupId;
            BuildAdjacencyList();
            SpatialGrid = new SpatialGrid2D(config.SpatialGridCellSize);
            SpatialGrid.Build(Nodes);
            IsGenerated = true;
            LastGenerateTime = Time.time;
        }

        /// <summary>
        /// 清除现有图数据
        /// </summary>
        public void ClearGraph()
        {
            if (!buildInProgress && committedSearchSnapshot != null)
            {
                graphRevision++;
                committedSearchSnapshot = null;
                committedLinkBindings = Array.Empty<PlatformLinkData>();
            }

            Nodes.Clear();
            NodeIdToIndex.Clear();
            Links.Clear();
            SurfaceSegments.Clear();
            surfaceSegmentsById.Clear();
            AdjacencyList.Clear();
            nextNodeId = 0;
            IsGenerated = false;
            SpatialGrid?.Clear();
            SpatialGrid = null;
            nextSurfaceGroupId = 0;
            heightTransitionCandidateChecks = 0;
            heightTransitionIntervalQueryCount = 0;
            _heightTransitionNodeBuckets.Clear();
            heightTransitionNodeBucketsBuilt = false;
            _surfaceNodeBuckets.Clear();
            surfaceNodeBucketsBuilt = false;
        }

        private void EnsureGraphIdentity()
        {
            if (graphIdentity != 0)
                return;

            graphIdentity = Interlocked.Increment(ref nextGraphIdentity);
        }

        /// <summary>
        /// 扫描区域内的平台碰撞体
        /// </summary>
        private List<Collider2D> ScanPlatformColliders()
        {
            var result = new List<Collider2D>();

            // 使用 OverlapBox 扫描
            var colliders = Physics2D.OverlapBoxAll(
                config.ScanCenter,
                config.ScanSize,
                0f,
                config.AllPlatformLayers
            );

            foreach (var col in colliders)
            {
                // 过滤无效碰撞体
                if (col == null || !col.enabled) continue;

                // 支持的碰撞体类型：
                // - CompositeCollider2D: Tilemap 使用 "Used by Composite" 时生成
                // - TilemapCollider2D: Tilemap 直接碰撞体
                // - BoxCollider2D, EdgeCollider2D, PolygonCollider2D: 普通平台
                if (col is CompositeCollider2D ||
                    col is UnityEngine.Tilemaps.TilemapCollider2D ||
                    col is BoxCollider2D || col is EdgeCollider2D || col is PolygonCollider2D)
                {
                    result.Add(col);
                }
            }

            return result;
        }

        /// <summary>
        /// 为单个平台生成节点
        /// </summary>
        private void GenerateNodesForPlatform(Collider2D collider)
        {
            // 判断是否是单向平台
            bool isOneWay = ((1 << collider.gameObject.layer) & config.OneWayPlatformLayer) != 0;

            // 根据碰撞体类型分别处理
            if (collider is CompositeCollider2D composite)
            {
                GenerateNodesForCompositeCollider(composite, isOneWay);
            }
            else if (collider is UnityEngine.Tilemaps.TilemapCollider2D tilemapCollider)
            {
                // TilemapCollider2D: 使用射线扫描方式生成节点
                GenerateNodesForTilemapCollider(tilemapCollider, isOneWay);
            }
            else if (collider is PolygonCollider2D polygon)
            {
                GenerateNodesForPolygonCollider(polygon, isOneWay);
            }
            else
            {
                // BoxCollider2D, EdgeCollider2D 等使用简单的 bounds 处理
                GenerateNodesForSimplePlatform(collider, collider.bounds, isOneWay);
            }
        }

        /// <summary>
        /// 为 TilemapCollider2D 生成节点（使用射线扫描）
        /// Tilemap 的形状复杂，使用从上向下的射线扫描来检测可站立表面
        /// </summary>
        private void GenerateNodesForTilemapCollider(UnityEngine.Tilemaps.TilemapCollider2D tilemapCollider, bool isOneWay)
        {
            // A spacing-sized sweep can visit the same X twice for a short
            // span (for example a single 1x1 tile). Use the tile columns as
            // continuous boundaries so those spans retain a real interior.
            if (TryGenerateNodesForTilemapCells(tilemapCollider, isOneWay))
                return;

            var bounds = tilemapCollider.bounds;
            float nodeSpacing = config.ActualNodeSpacing;

            // 从左到右扫描
            float startX = bounds.min.x + config.EdgeInset;
            float endX = bounds.max.x - config.EdgeInset;
            float scanY = bounds.max.y + 1f; // 从顶部上方开始向下扫描

            // 记录上一个检测到的表面 Y 坐标，用于检测边缘
            float? lastSurfaceY = null;
            float lastX = startX;
            float currentSegmentLeftX = startX;
            int currentSurfaceGroupId = -1;

            for (float x = startX; x <= endX; x += nodeSpacing)
            {
                // 从上向下发射射线检测表面
                Vector2 rayOrigin = new Vector2(x, scanY);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, bounds.size.y + 2f, config.AllPlatformLayers);

                if (hit.collider == tilemapCollider)
                {
                    float surfaceY = hit.point.y;

                    // 检测是否是新平台段的开始（左边缘）
                    if (!lastSurfaceY.HasValue || Mathf.Abs(surfaceY - lastSurfaceY.Value) > 0.5f)
                    {
                        // 如果之前有平台且现在高度变化大，说明上一段结束了（右边缘）
                        if (lastSurfaceY.HasValue && Mathf.Abs(surfaceY - lastSurfaceY.Value) > 0.5f)
                        {
                            Vector3 rightEdgePos = new Vector3(lastX, lastSurfaceY.Value, 0f);
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay, currentSurfaceGroupId));
                            AddSurfaceEdgeCache(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
                            AddBodySafeLandingNodes(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
                        }

                        // 新平台段的左边缘
                        currentSurfaceGroupId = AllocateSurfaceGroupId();
                        currentSegmentLeftX = x;
                        Vector3 leftEdgePos = new Vector3(x, surfaceY, 0f);
                        AddNode(PlatformNodeData.CreateEdge(nextNodeId++, leftEdgePos, tilemapCollider, true, isOneWay, currentSurfaceGroupId));
                    }
                    else
                    {
                        // 同一平台段的中间表面节点
                        Vector3 surfacePos = new Vector3(x, surfaceY, 0f);
                        AddNode(PlatformNodeData.CreateSurface(nextNodeId++, surfacePos, tilemapCollider, isOneWay, currentSurfaceGroupId));
                    }

                    lastSurfaceY = surfaceY;
                    lastX = x;
                }
                else
                {
                    // 没有检测到表面，如果之前有平台，这是右边缘
                    if (lastSurfaceY.HasValue)
                    {
                        Vector3 rightEdgePos = new Vector3(lastX, lastSurfaceY.Value, 0f);
                        AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay, currentSurfaceGroupId));
                        AddSurfaceEdgeCache(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
                        AddBodySafeLandingNodes(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
                        lastSurfaceY = null;
                        currentSurfaceGroupId = -1;
                    }
                }
            }

            // 处理最后一个平台段的右边缘
            if (lastSurfaceY.HasValue)
            {
                Vector3 rightEdgePos = new Vector3(lastX, lastSurfaceY.Value, 0f);
                AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay, currentSurfaceGroupId));
                AddSurfaceEdgeCache(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
                AddBodySafeLandingNodes(currentSegmentLeftX, lastX, lastSurfaceY.Value, tilemapCollider, isOneWay, currentSurfaceGroupId);
            }
        }

        private bool TryGenerateNodesForTilemapCells(
            TilemapCollider2D tilemapCollider,
            bool isOneWay)
        {
            Tilemap tilemap = tilemapCollider.GetComponent<Tilemap>();
            if (tilemap == null)
                return false;

            BoundsInt cellBounds = tilemap.cellBounds;
            if (cellBounds.size.x <= 0)
                return false;

            Bounds colliderBounds = tilemapCollider.bounds;
            if (colliderBounds.size.x <= SurfaceClipEpsilon)
                return false;

            Physics2D.SyncTransforms();
            float scanY = colliderBounds.max.y + 1f;
            float rayLength = Mathf.Max(2f, colliderBounds.size.y + 2f);
            bool hasSpan = false;
            bool generatedAnySpan = false;
            float spanLeft = 0f;
            float spanRight = 0f;
            float spanY = 0f;
            bool sawSurface = false;

            for (int cellX = cellBounds.xMin; cellX < cellBounds.xMax; cellX++)
            {
                Vector3 cellOrigin = tilemap.CellToWorld(
                    new Vector3Int(cellX, cellBounds.yMin, cellBounds.zMin));
                Vector3 nextCellOrigin = tilemap.CellToWorld(
                    new Vector3Int(cellX + 1, cellBounds.yMin, cellBounds.zMin));
                float cellLeft = Mathf.Min(cellOrigin.x, nextCellOrigin.x);
                float cellRight = Mathf.Max(cellOrigin.x, nextCellOrigin.x);
                if (cellRight - cellLeft <= SurfaceClipEpsilon ||
                    cellRight <= colliderBounds.min.x ||
                    cellLeft >= colliderBounds.max.x)
                {
                    continue;
                }

                float sampleX = (cellLeft + cellRight) * 0.5f;
                RaycastHit2D hit = Physics2D.Raycast(
                    new Vector2(sampleX, scanY),
                    Vector2.down,
                    rayLength,
                    config.AllPlatformLayers);
                if (hit.collider == tilemapCollider)
                {
                    sawSurface = true;
                    float surfaceY = hit.point.y;
                    bool continuesSpan = hasSpan &&
                                         Mathf.Abs(surfaceY - spanY) <= 0.5f &&
                                         Mathf.Abs(cellLeft - spanRight) <= 0.01f;
                    if (continuesSpan)
                    {
                        spanRight = cellRight;
                    }
                    else
                    {
                        if (hasSpan)
                        {
                            generatedAnySpan |= GenerateNodesForEdge(
                                spanLeft,
                                spanRight,
                                spanY,
                                tilemapCollider,
                                isOneWay) >= 0;
                        }

                        spanLeft = cellLeft;
                        spanRight = cellRight;
                        spanY = surfaceY;
                        hasSpan = true;
                    }
                }
                else if (hasSpan)
                {
                    generatedAnySpan |= GenerateNodesForEdge(
                        spanLeft,
                        spanRight,
                        spanY,
                        tilemapCollider,
                        isOneWay) >= 0;
                    hasSpan = false;
                }
            }

            if (hasSpan)
            {
                generatedAnySpan |= GenerateNodesForEdge(
                    spanLeft,
                    spanRight,
                    spanY,
                    tilemapCollider,
                    isOneWay) >= 0;
            }

            return sawSurface;
        }

        /// <summary>
        /// 为 CompositeCollider2D 生成节点（支持多路径）
        /// </summary>
        private void GenerateNodesForCompositeCollider(CompositeCollider2D composite, bool isOneWay)
        {
            int pathCount = composite.pathCount;

            for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
            {
                _pathPointsCache.Clear();
                int pointCount = composite.GetPath(pathIndex, _pathPointsCache);

                if (pointCount < 2) continue;

                // 提取该路径的顶部边缘并生成节点
                GenerateNodesForPath(_pathPointsCache, composite, isOneWay);
            }
        }

        /// <summary>
        /// 为 PolygonCollider2D 生成节点（支持多路径）
        /// </summary>
        private void GenerateNodesForPolygonCollider(PolygonCollider2D polygon, bool isOneWay)
        {
            int pathCount = polygon.pathCount;

            for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
            {
                var points = polygon.GetPath(pathIndex);
                if (points.Length < 2) continue;

                // 转换为世界坐标
                _pathPointsCache.Clear();
                var transform = polygon.transform;
                foreach (var localPoint in points)
                {
                    _pathPointsCache.Add(transform.TransformPoint(localPoint));
                }

                GenerateNodesForPath(_pathPointsCache, polygon, isOneWay);
            }
        }

        /// <summary>
        /// 从路径点中提取顶部边缘并生成节点
        /// </summary>
        private void GenerateNodesForPath(List<Vector2> worldPoints, Collider2D collider, bool isOneWay)
        {
            if (worldPoints.Count < 2) return;

            // 找到顶部边缘：遍历所有边，找出近似水平且位于顶部的边
            var topEdges = FindTopEdges(worldPoints);
            var groupedTopEdges = new List<(float left, float right, float y, int surfaceGroupId)>(topEdges.Count);

            foreach (var edge in topEdges)
            {
                int surfaceGroupId = GenerateNodesForEdge(edge.left, edge.right, edge.y, collider, isOneWay);
                groupedTopEdges.Add((edge.left, edge.right, edge.y, surfaceGroupId));
            }

            // 检测高度变化处并生成额外边缘节点（用于侧面突出平台跳跃）
            GenerateHeightTransitionNodes(groupedTopEdges, collider, isOneWay);
        }

        /// <summary>
        /// 检测高度变化处并生成额外边缘节点
        /// 解决侧面墙壁突出平台无法生成 Jump 链接的问题
        ///
        /// 场景示意：
        ///     │       │
        ///     │  ┌────┤  ← 上层突出平台 (upper)
        ///     │  │    │
        ///     │──┘    │     ← 这里需要额外的边缘节点！
        ///     │       │
        /// ────┴───────┴────  ← 下层平台 (lower)
        ///
        /// 在下层平台的 upper.left 和 upper.right 位置生成额外边缘节点，
        /// 使得 JumpLinkCalculator 能在水平距离内找到跳跃目标。
        /// </summary>
        private void GenerateHeightTransitionNodes(
            List<(float left, float right, float y, int surfaceGroupId)> edges,
            Collider2D collider,
            bool isOneWay)
        {
            if (edges == null || edges.Count < 2)
                return;

            var sortedByY = new List<HeightTransitionEdge>(edges.Count);
            foreach (var edge in edges)
            {
                sortedByY.Add(new HeightTransitionEdge(
                    edge.left,
                    edge.right,
                    edge.y,
                    collider,
                    isOneWay,
                    edge.surfaceGroupId));
            }

            sortedByY.Sort((a, b) => a.Y.CompareTo(b.Y));
            GenerateDownwardHeightTransitionNodes(
                sortedByY,
                Mathf.Max(0.05f, config.EdgeInset * 0.5f),
                config.EdgeInset);
        }

        private void GenerateDownwardHeightTransitionNodes(
            List<HeightTransitionEdge> sortedByY,
            float ledgeExitOffset,
            float inset)
        {
            EnsureHeightTransitionNodeBucketIndex();

            var queryPoints = new List<float>(sortedByY.Count * 2);
            for (int i = 0; i < sortedByY.Count; i++)
            {
                HeightTransitionEdge upper = sortedByY[i];
                queryPoints.Add(upper.Left - ledgeExitOffset);
                queryPoints.Add(upper.Right + ledgeExitOffset);
            }

            var lowerIntervals = new HeightTransitionIntervalIndex(queryPoints);
            int nextLowerIndex = 0;
            for (int upperIndex = 0; upperIndex < sortedByY.Count; upperIndex++)
            {
                HeightTransitionEdge upper = sortedByY[upperIndex];
                while (nextLowerIndex < upperIndex &&
                       upper.Y - sortedByY[nextLowerIndex].Y >= HeightTransitionMinimumDifference)
                {
                    HeightTransitionEdge lower = sortedByY[nextLowerIndex];
                    lowerIntervals.AddInterval(
                        nextLowerIndex,
                        lower.Left + inset,
                        lower.Right - inset);
                    nextLowerIndex++;
                }

                AddDownwardHeightTransitionAnchors(
                    sortedByY,
                    lowerIntervals,
                    upper.Left - ledgeExitOffset,
                    isLeftEdge: true,
                    inset);
                AddDownwardHeightTransitionAnchors(
                    sortedByY,
                    lowerIntervals,
                    upper.Right + ledgeExitOffset,
                    isLeftEdge: false,
                    inset);
            }
        }

        private void AddDownwardHeightTransitionAnchors(
            List<HeightTransitionEdge> sortedByY,
            HeightTransitionIntervalIndex lowerIntervals,
            float landingX,
            bool isLeftEdge,
            float inset)
        {
            QueryHeightTransitionIntervals(lowerIntervals, landingX);
            for (int i = 0; i < _heightTransitionCandidatesCache.Count; i++)
            {
                HeightTransitionEdge lower = sortedByY[_heightTransitionCandidatesCache[i]];
                if (!IsInsideHeightTransitionInterior(landingX, lower, inset))
                    continue;

                AddHeightTransitionEdgeNode(lower, landingX, isLeftEdge);
            }
        }

        private void QueryHeightTransitionIntervals(
            HeightTransitionIntervalIndex index,
            float queryX)
        {
            _heightTransitionCandidatesCache.Clear();
            index.Query(queryX, _heightTransitionCandidatesCache);
            heightTransitionIntervalQueryCount++;
            heightTransitionCandidateChecks += _heightTransitionCandidatesCache.Count;
        }

        private static bool IsInsideHeightTransitionInterior(
            float x,
            HeightTransitionEdge edge,
            float inset)
        {
            return x > edge.Left + inset &&
                   x < edge.Right - inset;
        }

        private void AddHeightTransitionEdgeNode(
            HeightTransitionEdge edge,
            float x,
            bool isLeftEdge)
        {
            Vector3 position = new Vector3(x, edge.Y, 0f);
            if (HasEdgeNodeNearPosition(
                    position,
                    HeightTransitionNodeDedupTolerance,
                    edge.SurfaceGroupId,
                    isLeftEdge))
            {
                return;
            }

            AddNode(PlatformNodeData.CreateEdge(
                nextNodeId++,
                position,
                edge.Collider,
                isLeftEdge,
                edge.IsOneWay,
                edge.SurfaceGroupId,
                isTransitionAnchor: true));
        }

        private void EnsureHeightTransitionNodeBucketIndex()
        {
            if (heightTransitionNodeBucketsBuilt)
                return;

            _heightTransitionNodeBuckets.Clear();
            for (int i = 0; i < Nodes.Count; i++)
                AddHeightTransitionNodeToBucketIndex(Nodes[i]);

            heightTransitionNodeBucketsBuilt = true;
        }

        private void AddHeightTransitionNodeToBucketIndex(PlatformNodeData node)
        {
            int side = GetHeightTransitionNodeSide(node.NodeType);
            if (side < 0)
                return;

            var key = new HeightTransitionNodeBucketKey(
                node.SurfaceGroupId,
                side,
                GetHeightTransitionXBucket(node.Position.x));
            if (!_heightTransitionNodeBuckets.TryGetValue(key, out List<float> positions))
            {
                positions = new List<float>();
                _heightTransitionNodeBuckets.Add(key, positions);
            }

            positions.Add(node.Position.x);
        }

        private static int GetHeightTransitionNodeSide(PlatformNodeType nodeType)
        {
            switch (nodeType)
            {
                case PlatformNodeType.Surface:
                    return HeightTransitionSurfaceSide;
                case PlatformNodeType.LeftEdge:
                    return HeightTransitionLeftSide;
                case PlatformNodeType.RightEdge:
                    return HeightTransitionRightSide;
                default:
                    return -1;
            }
        }

        private static int GetHeightTransitionXBucket(float x)
        {
            return Mathf.FloorToInt(x / HeightTransitionNodeBucketSize);
        }

        private bool HasNodeNearPosition(
            Vector3 position,
            float threshold,
            int surfaceGroupId,
            int side)
        {
            EnsureHeightTransitionNodeBucketIndex();
            if (threshold < 0f)
                threshold = -threshold;

            float bucketSize = HeightTransitionNodeBucketSize;
            int minBucket = Mathf.FloorToInt((position.x - threshold) / bucketSize);
            int maxBucket = Mathf.FloorToInt((position.x + threshold) / bucketSize);
            for (int bucket = minBucket; bucket <= maxBucket; bucket++)
            {
                var key = new HeightTransitionNodeBucketKey(surfaceGroupId, side, bucket);
                if (!_heightTransitionNodeBuckets.TryGetValue(key, out List<float> positions))
                    continue;

                for (int i = 0; i < positions.Count; i++)
                {
                    if (Mathf.Abs(positions[i] - position.x) < threshold)
                        return true;
                }
            }

            return false;
        }

        private bool HasEdgeNodeNearPosition(
            Vector3 position,
            float threshold,
            int surfaceGroupId,
            bool isLeftEdge)
        {
            return HasNodeNearPosition(
                position,
                threshold,
                surfaceGroupId,
                isLeftEdge ? HeightTransitionLeftSide : HeightTransitionRightSide);
        }

        /// <summary>
        /// 全局高度转换节点生成（后处理）
        /// 检测跨 Collider 的高度交界，在下层平台生成额外边缘节点
        ///
        /// 与 GenerateHeightTransitionNodes 的区别：
        /// - GenerateHeightTransitionNodes: 只处理同一 Collider 同一路径内的边缘
        /// - GenerateGlobalHeightTransitionNodes: 处理所有边缘（跨 Collider）
        /// </summary>
        private void GenerateGlobalHeightTransitionNodes()
        {
            if (_allEdgesCache.Count < 2)
                return;

            var sortedByY = new List<HeightTransitionEdge>(_allEdgesCache.Count);
            for (int i = 0; i < _allEdgesCache.Count; i++)
            {
                var edge = _allEdgesCache[i];
                sortedByY.Add(new HeightTransitionEdge(
                    edge.left,
                    edge.right,
                    edge.y,
                    edge.collider,
                    edge.isOneWay,
                    edge.surfaceGroupId));
            }

            sortedByY.Sort((a, b) => a.Y.CompareTo(b.Y));
            float inset = config.EdgeInset;
            float ledgeExitOffset = Mathf.Max(0.05f, config.EdgeInset * 0.5f);

            // Both directions use the same Y sweep and X interval index. In
            // particular, no jump-height policy belongs in graph generation:
            // JumpLinkCalculator applies its own MaxJump/MaxFall limits later.
            GenerateDownwardHeightTransitionNodes(sortedByY, ledgeExitOffset, inset);
            GenerateUpwardHeightTransitionNodes(sortedByY, inset);
        }

        private void GenerateUpwardHeightTransitionNodes(
            List<HeightTransitionEdge> sortedByY,
            float inset)
        {
            EnsureHeightTransitionNodeBucketIndex();

            var queryPoints = new List<float>(sortedByY.Count * 2);
            for (int i = 0; i < sortedByY.Count; i++)
            {
                queryPoints.Add(sortedByY[i].Left);
                queryPoints.Add(sortedByY[i].Right);
            }

            var upperIntervals = new HeightTransitionIntervalIndex(queryPoints);
            int nextUpperIndex = sortedByY.Count - 1;
            float intervalExpansion = HeightTransitionSafeLandingContactTolerance +
                                       HeightTransitionIndexCoordinateEpsilon;

            for (int lowerIndex = sortedByY.Count - 1; lowerIndex >= 0; lowerIndex--)
            {
                HeightTransitionEdge lower = sortedByY[lowerIndex];
                while (nextUpperIndex > lowerIndex &&
                       sortedByY[nextUpperIndex].Y - lower.Y >= HeightTransitionMinimumDifference)
                {
                    HeightTransitionEdge upper = sortedByY[nextUpperIndex];
                    upperIntervals.AddInterval(
                        nextUpperIndex,
                        upper.Left - intervalExpansion,
                        upper.Right + intervalExpansion);
                    nextUpperIndex--;
                }

                AddUpwardHeightTransitionAnchors(
                    sortedByY,
                    upperIntervals,
                    lower.Left,
                    isLowerLeftEdge: true,
                    inset);
                AddUpwardHeightTransitionAnchors(
                    sortedByY,
                    upperIntervals,
                    lower.Right,
                    isLowerLeftEdge: false,
                    inset);
            }
        }

        private void AddUpwardHeightTransitionAnchors(
            List<HeightTransitionEdge> sortedByY,
            HeightTransitionIntervalIndex upperIntervals,
            float lowerEdgeX,
            bool isLowerLeftEdge,
            float inset)
        {
            QueryHeightTransitionIntervals(upperIntervals, lowerEdgeX);
            for (int i = 0; i < _heightTransitionCandidatesCache.Count; i++)
            {
                HeightTransitionEdge upper = sortedByY[_heightTransitionCandidatesCache[i]];

                if (IsInsideHeightTransitionInterior(lowerEdgeX, upper, inset))
                {
                    AddHeightTransitionEdgeNode(
                        upper,
                        lowerEdgeX,
                        isLowerLeftEdge);
                }

                // Keep the old small contact band used for safe step-up
                // surface anchors; the interval index is deliberately expanded
                // by that band before this exact predicate is applied.
                if (CanAddSafeStepUpLandingSurfaceAnchor(
                        lowerEdgeX,
                        isLowerLeftEdge,
                        upper))
                {
                    AddSafeStepUpLandingSurfaceAnchor(
                        lowerEdgeX,
                        isLowerLeftEdge,
                        upper);
                }
            }
        }

        private static bool CanAddSafeStepUpLandingSurfaceAnchor(
            float lowerEdgeX,
            bool isLowerLeftEdge,
            HeightTransitionEdge upper)
        {
            bool canLandFromLeftEdge = isLowerLeftEdge &&
                                      lowerEdgeX <= upper.Right + HeightTransitionSafeLandingContactTolerance &&
                                      lowerEdgeX > upper.Left + HeightTransitionSafeLandingContactTolerance;
            bool canLandFromRightEdge = !isLowerLeftEdge &&
                                       lowerEdgeX >= upper.Left - HeightTransitionSafeLandingContactTolerance &&
                                       lowerEdgeX < upper.Right - HeightTransitionSafeLandingContactTolerance;
            return canLandFromLeftEdge || canLandFromRightEdge;
        }

        private void AddSafeStepUpLandingSurfaceAnchor(
            float lowerEdgeX,
            bool isLowerLeftEdge,
            HeightTransitionEdge upper)
        {
            float safeInset = Mathf.Max(config.CharacterRadius, config.EdgeInset) + 0.15f;
            if (upper.Right - upper.Left <= safeInset * 2f)
                return;

            bool canLandFromLeftEdge = isLowerLeftEdge &&
                                      lowerEdgeX <= upper.Right + HeightTransitionSafeLandingContactTolerance &&
                                      lowerEdgeX > upper.Left + HeightTransitionSafeLandingContactTolerance;
            bool canLandFromRightEdge = !isLowerLeftEdge &&
                                       lowerEdgeX >= upper.Left - HeightTransitionSafeLandingContactTolerance &&
                                       lowerEdgeX < upper.Right - HeightTransitionSafeLandingContactTolerance;
            if (!canLandFromLeftEdge && !canLandFromRightEdge)
                return;

            float desiredX = isLowerLeftEdge
                ? lowerEdgeX - safeInset
                : lowerEdgeX + safeInset;
            float landingX = Mathf.Clamp(desiredX, upper.Left + safeInset, upper.Right - safeInset);
            Vector3 landingPos = new Vector3(landingX, upper.Y, 0f);

            if (HasNodeNearPosition(
                    landingPos,
                    0.1f,
                    upper.SurfaceGroupId,
                    HeightTransitionSurfaceSide))
                return;

            AddNode(PlatformNodeData.CreateSurface(
                nextNodeId++,
                landingPos,
                upper.Collider,
                upper.IsOneWay,
                upper.SurfaceGroupId));
        }

        /// <summary>
        /// 判断多边形顶点是否为顺时针顺序
        /// 使用 Shoelace 公式计算有符号面积
        /// </summary>
        private bool IsClockwise(List<Vector2> points)
        {
            float sum = 0f;
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % count];
                sum += (p2.x - p1.x) * (p2.y + p1.y);
            }
            return sum > 0; // 正值 = 顺时针
        }

        /// <summary>
        /// 从多边形路径中找出顶部边缘
        /// 使用混合检测：优先射线检测，失败时使用法线方向判断
        /// </summary>
        private List<(float left, float right, float y)> FindTopEdges(List<Vector2> points)
        {
            var edges = new List<(float left, float right, float y)>();
            const float slopeThreshold = 0.5f; // 斜率阈值，放宽以支持斜坡
            const float mergeThreshold = 0.1f; // Y 坐标合并阈值
            const float standingHeight = 0.5f; // 降低检测高度，避免被墙壁阻挡
            const float rayLength = 1.0f;

            if (points.Count < 3) return edges;

            // 判断顶点顺序（用于法线方向计算）
            bool isClockwise = IsClockwise(points);
            int count = points.Count;

            for (int i = 0; i < count; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % count];

                // 计算边的水平跨度和垂直跨度
                float dx = Mathf.Abs(p2.x - p1.x);
                float dy = Mathf.Abs(p2.y - p1.y);

                // 跳过垂直边或太短的边
                if (dx < 0.1f) continue;

                // 检查是否近似水平
                float slope = dy / dx;
                if (slope > slopeThreshold) continue;

                float midX = (p1.x + p2.x) / 2f;
                float midY = (p1.y + p2.y) / 2f;

                // 方法1: 射线检测（优先）
                Vector2 rayOrigin = new Vector2(midX, midY + standingHeight);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayLength, config.AllPlatformLayers);

                bool isTopEdge = hit.collider != null &&
                                 Mathf.Abs(hit.point.y - midY) < 0.3f &&
                                 (rayOrigin.y - hit.point.y) > standingHeight * 0.5f;

                // 方法2: 法线方向判断（备选，用于射线被遮挡的情况）
                if (!isTopEdge)
                {
                    Vector2 edgeDir = p2 - p1;
                    // 根据顶点顺序调整法线方向
                    Vector2 normal = isClockwise
                        ? new Vector2(-edgeDir.y, edgeDir.x).normalized  // 顺时针：左手法则
                        : new Vector2(edgeDir.y, -edgeDir.x).normalized; // 逆时针：右手法则

                    isTopEdge = normal.y > 0.7f;
                }

                if (isTopEdge)
                {
                    float edgeY = midY;
                    float left = Mathf.Min(p1.x, p2.x);
                    float right = Mathf.Max(p1.x, p2.x);
                    edges.Add((left, right, edgeY));
                }
            }

            // 合并相邻的边，并去除 Y 坐标过于接近的重复边
            var merged = MergeAdjacentEdges(edges, mergeThreshold);
            return DeduplicateCloseEdges(merged, 0.5f);
        }

        /// <summary>
        /// 去除 Y 坐标过于接近的重复边（保留较高的那条）
        /// </summary>
        private List<(float left, float right, float y)> DeduplicateCloseEdges(
            List<(float left, float right, float y)> edges, float yThreshold)
        {
            if (edges.Count <= 1) return edges;

            // 按 X 范围分组，检查是否有 Y 坐标过于接近的边
            var result = new List<(float left, float right, float y)>();
            var sorted = new List<(float left, float right, float y)>(edges);
            sorted.Sort((a, b) => a.left.CompareTo(b.left));

            foreach (var edge in sorted)
            {
                bool isDuplicate = false;
                for (int i = 0; i < result.Count; i++)
                {
                    var existing = result[i];
                    // 检查 X 范围是否重叠
                    bool xOverlap = edge.left < existing.right && edge.right > existing.left;
                    // 检查 Y 是否过于接近
                    bool yClose = Mathf.Abs(edge.y - existing.y) < yThreshold;

                    if (xOverlap && yClose)
                    {
                        // 保留 Y 较高的那条
                        if (edge.y > existing.y)
                        {
                            result[i] = edge;
                        }
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    result.Add(edge);
                }
            }

            return result;
        }




        /// <summary>
        /// 合并相邻的顶部边缘
        /// </summary>
        private List<(float left, float right, float y)> MergeAdjacentEdges(
            List<(float left, float right, float y)> edges, float threshold)
        {
            if (edges.Count <= 1) return edges;

            // 按 Y 坐标和 X 坐标排序
            edges.Sort((a, b) =>
            {
                int yCompare = b.y.CompareTo(a.y); // Y 降序
                return yCompare != 0 ? yCompare : a.left.CompareTo(b.left);
            });

            var merged = new List<(float left, float right, float y)>();
            var current = edges[0];

            for (int i = 1; i < edges.Count; i++)
            {
                var next = edges[i];

                // 检查是否可以合并（Y 坐标接近且 X 范围重叠或相邻）
                if (Mathf.Abs(current.y - next.y) < threshold &&
                    next.left <= current.right + threshold)
                {
                    // 合并
                    current = (
                        Mathf.Min(current.left, next.left),
                        Mathf.Max(current.right, next.right),
                        (current.y + next.y) / 2f
                    );
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }
            merged.Add(current);

            return merged;
        }

        /// <summary>
        /// 为单条顶部边缘生成节点
        /// </summary>
        // surface-clipping：采样行走面前按上方是否被实心盖住裁成裸露子段，每段独立 surface+Left/RightEdge,
        // 避免在重叠实心台阶下生成"埋点"+穿实心体的 Walk link（修"图说可达但执行失败"缺口）。
        private const float SurfaceClipProbeHeight = 0.5f;
        private const float SurfaceClipMaxStep = 0.5f;
        private const float SurfaceClipMinStep = 0.05f;
        private const float SurfaceClipEpsilon = 0.001f;
        private readonly List<(float left, float right)> _surfaceClipSpansCache = new List<(float, float)>(8);
        private readonly List<(float left, float right, float y, int surfaceGroupId)> _generatedEdgeSpansCache
            = new List<(float, float, float, int)>(8);

        private int GenerateNodesForEdge(float left, float right, float y, Collider2D collider, bool isOneWay)
        {
            _generatedEdgeSpansCache.Clear();

            if (right < left)
            {
                float temp = left;
                left = right;
                right = temp;
            }

            Physics2D.SyncTransforms();
            CollectExposedSurfaceSpans(left, right, y, collider);

            int firstSurfaceGroupId = -1;
            foreach (var span in _surfaceClipSpansCache)
            {
                int surfaceGroupId = GenerateNodesForExposedSpan(span.left, span.right, y, collider, isOneWay);
                if (surfaceGroupId < 0)
                    continue;

                if (firstSurfaceGroupId < 0)
                    firstSurfaceGroupId = surfaceGroupId;

                _generatedEdgeSpansCache.Add((span.left, span.right, y, surfaceGroupId));
            }

            return firstSurfaceGroupId;
        }

        private void CollectExposedSurfaceSpans(float left, float right, float y, Collider2D collider)
        {
            _surfaceClipSpansCache.Clear();

            if (right - left <= SurfaceClipEpsilon)
                return;

            float scanStep = Mathf.Max(
                SurfaceClipMinStep,
                Mathf.Min(config.ActualNodeSpacing, SurfaceClipMaxStep));

            bool previousBuried = IsSurfaceBuriedAt(left, y, collider);
            float previousX = left;
            float exposedStart = previousBuried ? 0f : left;

            for (float x = left + scanStep; x < right; x += scanStep)
            {
                bool buried = IsSurfaceBuriedAt(x, y, collider);
                if (buried != previousBuried)
                {
                    float transitionX = FindSurfaceClipTransition(previousX, x, y, collider, previousBuried);
                    if (previousBuried)
                    {
                        exposedStart = transitionX;
                    }
                    else
                    {
                        AddExposedSurfaceSpan(exposedStart, transitionX);
                    }

                    previousBuried = buried;
                }

                previousX = x;
            }

            bool rightBuried = IsSurfaceBuriedAt(right, y, collider);
            if (rightBuried != previousBuried)
            {
                float transitionX = FindSurfaceClipTransition(previousX, right, y, collider, previousBuried);
                if (previousBuried)
                {
                    exposedStart = transitionX;
                }
                else
                {
                    AddExposedSurfaceSpan(exposedStart, transitionX);
                }

                previousBuried = rightBuried;
            }

            if (!previousBuried)
            {
                AddExposedSurfaceSpan(exposedStart, right);
            }
        }

        private float FindSurfaceClipTransition(
            float left,
            float right,
            float y,
            Collider2D collider,
            bool leftBuried)
        {
            for (int i = 0; i < 10; i++)
            {
                float mid = (left + right) * 0.5f;
                bool midBuried = IsSurfaceBuriedAt(mid, y, collider);
                if (midBuried == leftBuried)
                    left = mid;
                else
                    right = mid;
            }

            return (left + right) * 0.5f;
        }

        private bool IsSurfaceBuriedAt(float x, float y, Collider2D collider)
        {
            // Composite/Polygon colliders can contain both the lower floor and the step covering it.
            // The probe is above the sampled top edge, so a self hit here is still overhead solid.
            var cover = Physics2D.OverlapPoint(new Vector2(x, y + SurfaceClipProbeHeight), config.GroundLayer);
            return cover != null;
        }

        private void AddExposedSurfaceSpan(float left, float right)
        {
            if (right < left)
            {
                float temp = left;
                left = right;
                right = temp;
            }

            if (right - left <= SurfaceClipEpsilon)
                return;

            _surfaceClipSpansCache.Add((left, right));
        }

        private int GenerateNodesForExposedSpan(float left, float right, float y, Collider2D collider, bool isOneWay)
        {
            int surfaceGroupId = AllocateSurfaceGroupId();
            RegisterSurfaceSegment(surfaceGroupId, left, right, y, collider, isOneWay);

            // 缓存边缘数据，用于后续全局转换节点生成
            AddSurfaceEdgeCache(left, right, y, collider, isOneWay, surfaceGroupId);

            float width = right - left;
            float nodeSpacing = config.ActualNodeSpacing;

            // 平台太窄时仍保留左右边界锚点；如果角色刚好能站立，额外保留中心安全落点。
            if (width < config.MinPlatformWidth)
            {
                Vector3 leftPos = new Vector3(left, y, 0f);
                AddNode(PlatformNodeData.CreateEdge(nextNodeId++, leftPos, collider, true, isOneWay, surfaceGroupId));

                if (width > 0.05f)
                {
                    Vector3 rightPos = new Vector3(right, y, 0f);
                    AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightPos, collider, false, isOneWay, surfaceGroupId));
                }

                AddBodySafeLandingNodes(left, right, y, collider, isOneWay, surfaceGroupId);
                return surfaceGroupId;
            }

            // 生成左边缘节点
            Vector3 leftEdgePos = new Vector3(left + config.EdgeInset, y, 0f);
            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, leftEdgePos, collider, true, isOneWay, surfaceGroupId));

            // 生成右边缘节点
            Vector3 rightEdgePos = new Vector3(right - config.EdgeInset, y, 0f);
            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, collider, false, isOneWay, surfaceGroupId));

            AddPhysicalEdgeTransitionAnchors(left, right, y, collider, isOneWay, surfaceGroupId);

            // Jump landing uses the actor center, so preserve stable landing anchors independently
            // from traversal edges. This keeps edge/step/fall topology unchanged.
            AddBodySafeLandingNodes(left, right, y, collider, isOneWay, surfaceGroupId);

            // 生成中间表面节点
            float innerWidth = width - 2 * config.EdgeInset;
            int innerNodeCount = Mathf.FloorToInt(innerWidth / nodeSpacing);

            if (innerNodeCount > 0)
            {
                float actualSpacing = innerWidth / (innerNodeCount + 1);
                for (int i = 1; i <= innerNodeCount; i++)
                {
                    float x = left + config.EdgeInset + actualSpacing * i;
                    AddNode(PlatformNodeData.CreateSurface(
                        nextNodeId++,
                        new Vector3(x, y, 0f),
                        collider,
                        isOneWay,
                        surfaceGroupId));
                }
            }

            return surfaceGroupId;
        }

        private void AddBodySafeLandingNodes(
            float left,
            float right,
            float y,
            Collider2D collider,
            bool isOneWay,
            int surfaceGroupId)
        {
            float safeInset = Mathf.Max(0.05f, config.CharacterRadius + 0.05f);
            float leftSafeX = left + safeInset;
            float rightSafeX = right - safeInset;
            if (leftSafeX > rightSafeX + 0.001f)
                return;

            AddSurfaceNodeIfMissing(leftSafeX, y, collider, isOneWay, surfaceGroupId);
            if (rightSafeX - leftSafeX > 0.05f)
                AddSurfaceNodeIfMissing(rightSafeX, y, collider, isOneWay, surfaceGroupId);
        }

        private void AddSurfaceNodeIfMissing(
            float x,
            float y,
            Collider2D collider,
            bool isOneWay,
            int surfaceGroupId)
        {
            EnsureSurfaceNodeBucketIndex();

            Vector3 surfacePos = new Vector3(x, y, 0f);
            if (HasSurfaceNodeNearPosition(surfacePos, surfaceGroupId))
                return;

            AddNode(PlatformNodeData.CreateSurface(
                nextNodeId++,
                surfacePos,
                collider,
                isOneWay,
                surfaceGroupId));
        }

        private void AddPhysicalEdgeTransitionAnchors(
            float left,
            float right,
            float y,
            Collider2D collider,
            bool isOneWay,
            int surfaceGroupId)
        {
            if (config.EdgeInset <= 0.01f)
                return;

            Vector3 leftAnchor = new Vector3(left, y, 0f);
            if (!HasNodeNearPosition(
                    leftAnchor,
                    0.05f,
                    surfaceGroupId,
                    HeightTransitionLeftSide))
            {
                AddNode(PlatformNodeData.CreateEdge(
                    nextNodeId++,
                    leftAnchor,
                    collider,
                    true,
                    isOneWay,
                    surfaceGroupId,
                    isTransitionAnchor: true));
            }

            Vector3 rightAnchor = new Vector3(right, y, 0f);
            if (!HasNodeNearPosition(
                    rightAnchor,
                    0.05f,
                    surfaceGroupId,
                    HeightTransitionRightSide))
            {
                AddNode(PlatformNodeData.CreateEdge(
                    nextNodeId++,
                    rightAnchor,
                    collider,
                    false,
                    isOneWay,
                    surfaceGroupId,
                    isTransitionAnchor: true));
            }
        }

        private void EnsureSurfaceNodeBucketIndex()
        {
            if (surfaceNodeBucketsBuilt)
                return;

            _surfaceNodeBuckets.Clear();
            for (int i = 0; i < Nodes.Count; i++)
                AddSurfaceNodeToBucketIndex(Nodes[i]);

            surfaceNodeBucketsBuilt = true;
        }

        private void AddSurfaceNodeToBucketIndex(PlatformNodeData node)
        {
            var key = new SurfaceNodeBucketKey(
                node.SurfaceGroupId,
                GetSurfaceNodeBucket(node.Position.x),
                GetSurfaceNodeBucket(node.Position.y));
            if (!_surfaceNodeBuckets.TryGetValue(key, out List<Vector2> positions))
            {
                positions = new List<Vector2>();
                _surfaceNodeBuckets.Add(key, positions);
            }

            positions.Add(node.Position);
        }

        private static int GetSurfaceNodeBucket(float coordinate)
        {
            return Mathf.FloorToInt(coordinate / SurfaceNodeBucketSize);
        }

        private bool HasSurfaceNodeNearPosition(Vector3 position, int surfaceGroupId)
        {
            float threshold = SurfaceNodeDedupTolerance;
            int minXBucket = GetSurfaceNodeBucket(position.x - threshold);
            int maxXBucket = GetSurfaceNodeBucket(position.x + threshold);
            int minYBucket = GetSurfaceNodeBucket(position.y - threshold);
            int maxYBucket = GetSurfaceNodeBucket(position.y + threshold);

            for (int xBucket = minXBucket; xBucket <= maxXBucket; xBucket++)
            {
                for (int yBucket = minYBucket; yBucket <= maxYBucket; yBucket++)
                {
                    var key = new SurfaceNodeBucketKey(surfaceGroupId, xBucket, yBucket);
                    if (!_surfaceNodeBuckets.TryGetValue(key, out List<Vector2> positions))
                        continue;

                    for (int i = 0; i < positions.Count; i++)
                    {
                        Vector2 existing = positions[i];
                        if (Mathf.Abs(existing.x - position.x) <= threshold &&
                            Mathf.Abs(existing.y - position.y) <= threshold)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 为简单碰撞体生成节点（使用 bounds）
        /// </summary>
        private void GenerateNodesForSimplePlatform(Collider2D collider, Bounds bounds, bool isOneWay)
        {
            float left = bounds.min.x;
            float right = bounds.max.x;
            float top = bounds.max.y;

            GenerateNodesForEdge(left, right, top, collider, isOneWay);
        }

        /// <summary>
        /// 添加节点
        /// </summary>
        private void AddNode(PlatformNodeData node)
        {
            NodeIdToIndex[node.NodeId] = Nodes.Count;
            Nodes.Add(node);

            if (surfaceNodeBucketsBuilt)
                AddSurfaceNodeToBucketIndex(node);

            if (heightTransitionNodeBucketsBuilt)
                AddHeightTransitionNodeToBucketIndex(node);

            if (surfaceSegmentsById.TryGetValue(node.SurfaceGroupId, out var segment))
            {
                segment.NodeIds.Add(node.NodeId);
                if (node.NodeType == PlatformNodeType.LeftEdge && segment.LeftNodeId < 0)
                    segment.LeftNodeId = node.NodeId;
                else if (node.NodeType == PlatformNodeType.RightEdge && segment.RightNodeId < 0)
                    segment.RightNodeId = node.NodeId;
            }
        }

        private int AllocateSurfaceGroupId()
        {
            return nextSurfaceGroupId++;
        }

        private void RegisterSurfaceSegment(
            int groupId,
            float left,
            float right,
            float y,
            Collider2D collider,
            bool isOneWay)
        {
            if (groupId < 0 || surfaceSegmentsById.ContainsKey(groupId))
                return;

            var segment = new PlatformSurfaceSegment
            {
                GroupId = groupId,
                Collider = collider,
                Left = Mathf.Min(left, right),
                Right = Mathf.Max(left, right),
                Y = y,
                IsOneWay = isOneWay
            };

            surfaceSegmentsById[groupId] = segment;
            SurfaceSegments.Add(segment);
            AttachExistingNodesToSegment(segment);
        }

        public bool TryGetSurfaceSegment(int surfaceGroupId, out PlatformSurfaceSegment segment)
        {
            return surfaceSegmentsById.TryGetValue(surfaceGroupId, out segment);
        }

        public bool TryFindSurfaceSegmentAt(Vector3 position, float verticalTolerance, out PlatformSurfaceSegment segment)
        {
            foreach (var candidate in SurfaceSegments)
            {
                bool xInside = position.x >= candidate.MinX && position.x <= candidate.MaxX;
                bool yClose = Mathf.Abs(position.y - candidate.Y) <= verticalTolerance;
                if (!xInside || !yClose)
                    continue;

                segment = candidate;
                return true;
            }

            segment = default;
            return false;
        }

        public string BuildSurfaceSegmentDebug()
        {
            if (SurfaceSegments == null || SurfaceSegments.Count == 0)
                return "none";

            var parts = new List<string>(SurfaceSegments.Count);
            foreach (var segment in SurfaceSegments)
            {
                parts.Add($"{segment.Id}:x=[{segment.MinX:F2},{segment.MaxX:F2}],y={segment.Y:F2},oneWay={segment.IsOneWay}");
            }

            return string.Join(" | ", parts);
        }

        private void AttachExistingNodesToSegment(PlatformSurfaceSegment segment)
        {
            foreach (var node in Nodes)
            {
                if (node.SurfaceGroupId != segment.GroupId || segment.NodeIds.Contains(node.NodeId))
                    continue;

                segment.NodeIds.Add(node.NodeId);
                if (node.NodeType == PlatformNodeType.LeftEdge && segment.LeftNodeId < 0)
                    segment.LeftNodeId = node.NodeId;
                else if (node.NodeType == PlatformNodeType.RightEdge && segment.RightNodeId < 0)
                    segment.RightNodeId = node.NodeId;
            }
        }

        private void AddSurfaceEdgeCache(
            float left,
            float right,
            float y,
            Collider2D collider,
            bool isOneWay,
            int surfaceGroupId)
        {
            if (surfaceGroupId < 0)
                return;

            RegisterSurfaceSegment(surfaceGroupId, left, right, y, collider, isOneWay);
            _allEdgesCache.Add((left, right, y, collider, isOneWay, surfaceGroupId));
        }

        /// <summary>
        /// 生成同平台行走链接
        /// </summary>
        private void GenerateWalkLinks()
        {
            const float maxYDiff = 0.5f; // 同一行走平面的最大 Y 坐标差异
            const float maxXGap = 3f; // 相邻节点最大 X 间距（超过则不连接）

            // 按连续平台段分组节点。Composite/Tilemap 可能一个 Collider 包含多条平台段，
            // 所以不能再用 Collider + Y 作为同平台身份。
            var platformGroups = new Dictionary<int, List<int>>();

            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                if (node.PlatformCollider == null) continue;

                int groupId = node.SurfaceGroupId;
                if (groupId < 0)
                    groupId = -100000 - i;

                if (!platformGroups.ContainsKey(groupId))
                {
                    platformGroups[groupId] = new List<int>();
                }
                platformGroups[groupId].Add(i);
            }

            // 为每个平台层的节点生成行走链接
            foreach (var kvp in platformGroups)
            {
                var nodeIndices = kvp.Value;
                if (nodeIndices.Count < 2) continue;

                // 按 X 坐标排序
                nodeIndices.Sort((a, b) => Nodes[a].Position.x.CompareTo(Nodes[b].Position.x));

                // 相邻节点之间创建双向行走链接
                for (int i = 0; i < nodeIndices.Count - 1; i++)
                {
                    int fromIndex = nodeIndices[i];
                    int toIndex = nodeIndices[i + 1];

                    var fromNode = Nodes[fromIndex];
                    var toNode = Nodes[toIndex];

                    // 额外检查：X 间距不能太大，Y 差异不能太大
                    float xGap = Mathf.Abs(toNode.Position.x - fromNode.Position.x);
                    float yDiff = Mathf.Abs(toNode.Position.y - fromNode.Position.y);

                    if (xGap > maxXGap || yDiff > maxYDiff) continue;

                    float distance = Vector2.Distance(fromNode.Position, toNode.Position);

                    // 创建双向链接
                    Links.Add(PlatformLinkData.CreateWalk(fromNode.NodeId, toNode.NodeId, distance));
                    Links.Add(PlatformLinkData.CreateWalk(toNode.NodeId, fromNode.NodeId, distance));
                }
            }
        }

        /// <summary>
        /// 获取节点数据
        /// </summary>
        public PlatformNodeData? GetNode(int nodeId)
        {
            if (NodeIdToIndex.TryGetValue(nodeId, out int index))
            {
                return Nodes[index];
            }
            return null;
        }

        /// <summary>
        /// 查找指定平台上的最近节点（优先脚下平台）
        /// 解决召唤物在突出平台下方时错误选择头顶节点的问题
        /// </summary>
        /// <param name="position">查询位置</param>
        /// <param name="preferredPlatform">优先选择的平台（通常是脚下平台）</param>
        /// <param name="maxDistance">最大搜索距离</param>
        /// <returns>找到的节点，优先返回指定平台上的节点</returns>
        public PlatformNodeData? FindNearestNodeOnPlatform(Vector2 position, Collider2D preferredPlatform, float maxDistance)
        {
            // 1. 先找目标点所在的连续平台段。Composite/Tilemap 内可能有多个平台段，
            // 只按 Collider 找最近节点会把目标吸到同 Collider 的空平台上。
            if (preferredPlatform != null)
            {
                var groupNode = FindNearestNodeInSurfaceGroup(position, preferredPlatform, maxDistance);
                if (groupNode.HasValue)
                    return groupNode;
            }

            // 2. 回退到原逻辑：查找任意平台上的最近节点
            return FindNearestNode(position, maxDistance);
        }

        public int FindSurfaceGroupAt(Vector2 position, Collider2D preferredPlatform, float maxDistance)
        {
            var segment = FindSurfaceSegmentAt(position, preferredPlatform, maxDistance);
            return segment != null ? segment.GroupId : -1;
        }

        private PlatformNodeData? FindNearestNodeInSurfaceGroup(Vector2 position, Collider2D preferredPlatform, float maxDistance)
        {
            if (preferredPlatform == null)
                return null;

            var segment = FindSurfaceSegmentAt(position, preferredPlatform, maxDistance);
            if (segment == null)
                return null;

            PlatformNodeData? bestStandableOnGroup = null;
            float bestStandableDistance = maxDistance;
            PlatformNodeData? bestOnGroup = null;
            float bestNodeDistance = maxDistance;
            foreach (var node in Nodes)
            {
                if (node.SurfaceGroupId != segment.GroupId)
                    continue;

                float dist = Vector2.Distance(position, node.Position);
                if (dist < bestNodeDistance)
                {
                    bestNodeDistance = dist;
                    bestOnGroup = node;
                }

                if (!node.IsTransitionAnchor && dist < bestStandableDistance)
                {
                    bestStandableDistance = dist;
                    bestStandableOnGroup = node;
                }
            }

            return bestStandableOnGroup ?? bestOnGroup;
        }

        public PlatformSurfaceSegment FindSurfaceSegmentAt(Vector2 position, Collider2D preferredPlatform, float maxDistance)
        {
            PlatformSurfaceSegment bestContainingBelowSegment = null;
            float bestContainingBelowVertical = maxDistance;
            PlatformSurfaceSegment bestContainingSegment = null;
            float bestContainingVertical = maxDistance;
            const float surfaceAboveTolerance = 0.25f;

            foreach (var segment in SurfaceSegments)
            {
                if (segment.Collider != preferredPlatform)
                    continue;

                if (!segment.ContainsX(position.x, config.EdgeInset + 0.05f))
                    continue;

                float verticalDistance = Mathf.Abs(position.y - segment.Y);
                if (verticalDistance < bestContainingVertical)
                {
                    bestContainingVertical = verticalDistance;
                    bestContainingSegment = segment;
                }

                if (segment.Y <= position.y + surfaceAboveTolerance &&
                    verticalDistance < bestContainingBelowVertical)
                {
                    bestContainingBelowVertical = verticalDistance;
                    bestContainingBelowSegment = segment;
                }
            }

            if (bestContainingBelowSegment != null)
                return bestContainingBelowSegment;

            if (bestContainingSegment != null)
                return bestContainingSegment;

            PlatformSurfaceSegment bestBelowSegment = null;
            float bestBelowDistance = maxDistance;
            PlatformSurfaceSegment bestSegment = null;
            float bestDistance = maxDistance;
            foreach (var segment in SurfaceSegments)
            {
                if (segment.Collider != preferredPlatform)
                    continue;

                float distance = segment.DistanceTo(position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSegment = segment;
                }

                if (segment.Y <= position.y + surfaceAboveTolerance &&
                    distance < bestBelowDistance)
                {
                    bestBelowDistance = distance;
                    bestBelowSegment = segment;
                }
            }

            if (bestBelowSegment != null)
                return bestBelowSegment;

            return bestSegment;
        }

        /// <summary>
        /// 查找最近的节点（使用空间索引加速）
        /// </summary>
        public PlatformNodeData? FindNearestNode(Vector2 position, float maxDistance = float.MaxValue)
        {
            // 优先使用空间索引
            if (SpatialGrid != null)
            {
                return SpatialGrid.FindNearest(position, maxDistance);
            }

            // 回退到线性搜索
            PlatformNodeData? nearest = null;
            float nearestDist = maxDistance;

            foreach (var node in Nodes)
            {
                float dist = Vector2.Distance(position, node.Position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = node;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 查找指定范围内的所有节点（使用空间索引加速）
        /// </summary>
        public List<PlatformNodeData> FindNodesInRange(Vector2 position, float range)
        {
            // 优先使用空间索引
            if (SpatialGrid != null)
            {
                _nodesInRangeCache.Clear();
                SpatialGrid.FindNodesInRange(position, range, _nodesInRangeCache);
                return new List<PlatformNodeData>(_nodesInRangeCache);
            }

            // 回退到线性搜索
            var result = new List<PlatformNodeData>();

            foreach (var node in Nodes)
            {
                if (Vector2.Distance(position, node.Position) <= range)
                {
                    result.Add(node);
                }
            }

            return result;
        }

        /// <summary>
        /// 查找指定范围内的所有节点（无 GC 分配版本）
        /// </summary>
        /// <param name="position">查询位置</param>
        /// <param name="range">范围半径</param>
        /// <param name="results">结果列表（调用者提供）</param>
        public void FindNodesInRangeNonAlloc(Vector2 position, float range, List<PlatformNodeData> results)
        {
            results.Clear();

            if (SpatialGrid != null)
            {
                SpatialGrid.FindNodesInRange(position, range, results);
            }
            else
            {
                foreach (var node in Nodes)
                {
                    if (Vector2.Distance(position, node.Position) <= range)
                    {
                        results.Add(node);
                    }
                }
            }
        }

        /// <summary>
        /// 获取节点的所有出边链接（使用邻接表优化，O(1) 查询）
        /// </summary>
        public List<PlatformLinkData> GetOutgoingLinks(int nodeId)
        {
            // 优先使用邻接表（O(1) 查询）
            if (AdjacencyList.TryGetValue(nodeId, out var links))
            {
                return links;
            }

            // 回退到线性搜索（兼容旧代码路径）
            var result = new List<PlatformLinkData>();
            foreach (var link in Links)
            {
                if (link.FromNodeId == nodeId)
                {
                    result.Add(link);
                }
            }
            return result;
        }

        /// <summary>
        /// 构建邻接表（在所有链接生成后调用）
        /// 将 O(n) 的链接查询优化为 O(1)
        /// </summary>
        public void BuildAdjacencyList()
        {
            AdjacencyList.Clear();

            // 预分配每个节点的链接列表
            foreach (var node in Nodes)
            {
                AdjacencyList[node.NodeId] = new List<PlatformLinkData>();
            }

            // 填充邻接表
            foreach (var link in Links)
            {
                if (AdjacencyList.TryGetValue(link.FromNodeId, out var list))
                {
                    list.Add(link);
                }
            }
        }

        /// <summary>
        /// 添加链接并更新邻接表
        /// </summary>
        public void AddLink(PlatformLinkData link)
        {
            Links.Add(link);

            // 如果邻接表已构建，同步更新
            if (AdjacencyList.Count > 0)
            {
                if (!AdjacencyList.TryGetValue(link.FromNodeId, out var list))
                {
                    list = new List<PlatformLinkData>();
                    AdjacencyList[link.FromNodeId] = list;
                }
                list.Add(link);
            }
        }

        /// <summary>
        /// 生成详细诊断报告
        /// </summary>
#if ODIN_INSPECTOR
        [Button("输出详细诊断报告", ButtonSizes.Medium), PropertyOrder(100)]
#endif
        public void GenerateDiagnosticReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========== 平台图诊断报告 ==========");
            sb.AppendLine();

            // 1. 配置信息
            sb.AppendLine("[配置]");
            sb.AppendLine($"  扫描中心: {config.ScanCenter}");
            sb.AppendLine($"  扫描尺寸: {config.ScanSize}");
            sb.AppendLine($"  节点间距: {config.ActualNodeSpacing}");
            sb.AppendLine($"  GroundLayer: {config.GroundLayer.value} ({LayerMaskToNames(config.GroundLayer)})");
            sb.AppendLine($"  OneWayPlatformLayer: {config.OneWayPlatformLayer.value} ({LayerMaskToNames(config.OneWayPlatformLayer)})");
            sb.AppendLine($"  ObstacleLayer: {config.ObstacleLayer.value} ({LayerMaskToNames(config.ObstacleLayer)})");
            sb.AppendLine();

            // 2. 扫描到的碰撞体
            sb.AppendLine("[扫描到的平台碰撞体]");
            var colliders = ScanPlatformColliders();
            sb.AppendLine($"  共扫描到 {colliders.Count} 个碰撞体:");
            foreach (var col in colliders)
            {
                string colType = col.GetType().Name;
                string layer = LayerMask.LayerToName(col.gameObject.layer);
                var bounds = col.bounds;
                sb.AppendLine($"    - {col.gameObject.name} ({colType}) Layer={layer}");
                sb.AppendLine($"      Bounds: center={bounds.center}, size={bounds.size}");
                sb.AppendLine($"      Y范围: {bounds.min.y:F2} ~ {bounds.max.y:F2}");
            }
            sb.AppendLine();

            // 3. 节点统计
            sb.AppendLine("[节点统计]");
            sb.AppendLine($"  总节点数: {Nodes.Count}");

            // 按高度分组
            var nodesByHeight = new Dictionary<int, List<PlatformNodeData>>();
            foreach (var node in Nodes)
            {
                int y = Mathf.RoundToInt(node.Position.y);
                if (!nodesByHeight.ContainsKey(y))
                    nodesByHeight[y] = new List<PlatformNodeData>();
                nodesByHeight[y].Add(node);
            }

            var sortedHeights = new List<int>(nodesByHeight.Keys);
            sortedHeights.Sort();
            sb.AppendLine("  按高度分布:");
            foreach (var y in sortedHeights)
            {
                var nodesAtY = nodesByHeight[y];
                float minX = float.MaxValue, maxX = float.MinValue;
                foreach (var n in nodesAtY)
                {
                    if (n.Position.x < minX) minX = n.Position.x;
                    if (n.Position.x > maxX) maxX = n.Position.x;
                }
                sb.AppendLine($"    Y={y}: {nodesAtY.Count}个节点, X范围=[{minX:F1}, {maxX:F1}]");
            }
            sb.AppendLine();

            // 4. 链接统计
            sb.AppendLine("[链接统计]");
            int walkCount = 0, jumpCount = 0, fallCount = 0, dropCount = 0;
            foreach (var link in Links)
            {
                switch (link.LinkType)
                {
                    case PlatformLinkType.Walk: walkCount++; break;
                    case PlatformLinkType.Jump: jumpCount++; break;
                    case PlatformLinkType.Fall: fallCount++; break;
                    case PlatformLinkType.DropThrough: dropCount++; break;
                }
            }
            sb.AppendLine($"  Walk: {walkCount}, Jump: {jumpCount}, Fall: {fallCount}, DropThrough: {dropCount}");
            sb.AppendLine();

            // 5. 空间索引
            if (SpatialGrid != null)
            {
                sb.AppendLine("[空间索引]");
                sb.AppendLine($"  {SpatialGrid.GetDebugInfo()}");
            }

            sb.AppendLine("========== 报告结束 ==========");
            if (PathfindingLogSettings.EnableDetailedDiagnostics)
                Debug.Log(sb.ToString());
        }

        private string LayerMaskToNames(LayerMask mask)
        {
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if ((mask.value & (1 << i)) != 0)
                {
                    string name = LayerMask.LayerToName(i);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }
            return names.Count > 0 ? string.Join(", ", names) : "无";
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 绘制扫描区域
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawWireCube(config.ScanCenter, config.ScanSize);

            if (!IsGenerated) return;

            // 绘制节点
            foreach (var node in Nodes)
            {
                switch (node.NodeType)
                {
                    case PlatformNodeType.LeftEdge:
                    case PlatformNodeType.RightEdge:
                        Gizmos.color = Color.yellow;
                        break;
                    case PlatformNodeType.OneWay:
                        Gizmos.color = Color.cyan;
                        break;
                    default:
                        Gizmos.color = node.IsOneWay ? Color.cyan : Color.green;
                        break;
                }

                Gizmos.DrawSphere(node.Position, 0.2f);
            }

            // 绘制链接
            foreach (var link in Links)
            {
                var fromNode = GetNode(link.FromNodeId);
                var toNode = GetNode(link.ToNodeId);

                if (!fromNode.HasValue || !toNode.HasValue) continue;

                switch (link.LinkType)
                {
                    case PlatformLinkType.Walk:
                        Gizmos.color = Color.green;
                        break;
                    case PlatformLinkType.Jump:
                        Gizmos.color = Color.yellow;
                        break;
                    case PlatformLinkType.Fall:
                    case PlatformLinkType.DropThrough:
                        Gizmos.color = Color.blue;
                        break;
                }

                Gizmos.DrawLine(fromNode.Value.Position, toNode.Value.Position);
            }
        }
#endif
    }
}
