// PlatformGraphGenerator.cs
// 平台图生成器
// 扫描场景中的平台碰撞体，生成用于寻路的节点网络

using System.Collections.Generic;
using UnityEngine;
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

        /// <summary>邻接表：节点ID -> 出边链接列表（性能优化）</summary>
        public Dictionary<int, List<PlatformLinkData>> AdjacencyList { get; private set; } = new Dictionary<int, List<PlatformLinkData>>();

        /// <summary>是否已生成</summary>
        public bool IsGenerated { get; private set; }

        /// <summary>上次生成时间</summary>
        public float LastGenerateTime { get; private set; }

        /// <summary>空间索引</summary>
        public SpatialGrid2D SpatialGrid { get; private set; }

        private int nextNodeId = 0;

        // 缓存所有边缘数据，用于全局转换节点生成
        private readonly List<(float left, float right, float y, Collider2D collider, bool isOneWay)> _allEdgesCache
            = new List<(float, float, float, Collider2D, bool)>();

        // 复用 List 避免 GC
        private readonly List<Vector2> _pathPointsCache = new List<Vector2>(64);
        private readonly List<PlatformNodeData> _nodesInRangeCache = new List<PlatformNodeData>(32);

        /// <summary>
        /// 生成平台图
        /// </summary>
        public void GeneratePlatformGraph()
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

        /// <summary>
        /// 清除现有图数据
        /// </summary>
        public void ClearGraph()
        {
            Nodes.Clear();
            NodeIdToIndex.Clear();
            Links.Clear();
            AdjacencyList.Clear();
            nextNodeId = 0;
            IsGenerated = false;
            SpatialGrid?.Clear();
            SpatialGrid = null;
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
            var bounds = tilemapCollider.bounds;
            float nodeSpacing = config.ActualNodeSpacing;

            // 从左到右扫描
            float startX = bounds.min.x + config.EdgeInset;
            float endX = bounds.max.x - config.EdgeInset;
            float scanY = bounds.max.y + 1f; // 从顶部上方开始向下扫描

            // 记录上一个检测到的表面 Y 坐标，用于检测边缘
            float? lastSurfaceY = null;
            float lastX = startX;

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
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay));
                        }

                        // 新平台段的左边缘
                        Vector3 leftEdgePos = new Vector3(x, surfaceY, 0f);
                        AddNode(PlatformNodeData.CreateEdge(nextNodeId++, leftEdgePos, tilemapCollider, true, isOneWay));
                    }
                    else
                    {
                        // 同一平台段的中间表面节点
                        Vector3 surfacePos = new Vector3(x, surfaceY, 0f);
                        AddNode(PlatformNodeData.CreateSurface(nextNodeId++, surfacePos, tilemapCollider, isOneWay));
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
                        AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay));
                        lastSurfaceY = null;
                    }
                }
            }

            // 处理最后一个平台段的右边缘
            if (lastSurfaceY.HasValue)
            {
                Vector3 rightEdgePos = new Vector3(lastX, lastSurfaceY.Value, 0f);
                AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, tilemapCollider, false, isOneWay));
            }
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

            foreach (var edge in topEdges)
            {
                GenerateNodesForEdge(edge.left, edge.right, edge.y, collider, isOneWay);
            }

            // 检测高度变化处并生成额外边缘节点（用于侧面突出平台跳跃）
            GenerateHeightTransitionNodes(topEdges, collider, isOneWay);
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
        private void GenerateHeightTransitionNodes(List<(float left, float right, float y)> edges, Collider2D collider, bool isOneWay)
        {
            if (edges.Count < 2) return;

            // 按 Y 坐标升序排序
            var sortedByY = new List<(float left, float right, float y)>(edges);
            sortedByY.Sort((a, b) => a.y.CompareTo(b.y));

            // 检测相邻高度层的交界处
            for (int i = 0; i < sortedByY.Count; i++)
            {
                var lower = sortedByY[i];

                for (int j = i + 1; j < sortedByY.Count; j++)
                {
                    var upper = sortedByY[j];

                    // 高度差太大（超过最大跳跃高度），跳过
                    float heightDiff = upper.y - lower.y;
                    if (heightDiff > 8f) continue;

                    // 高度差太小（同一平面），跳过
                    if (heightDiff < 0.5f) continue;

                    // 检查上层平台的边缘是否在下层平台的 X 范围内
                    // 如果是，说明存在高度交界点，需要在下层平台生成额外边缘节点

                    // 上层左边缘在下层范围内 → 在下层的 upper.left 位置生成节点
                    if (upper.left > lower.left + config.EdgeInset && upper.left < lower.right - config.EdgeInset)
                    {
                        Vector3 transitionPos = new Vector3(upper.left, lower.y, 0f);
                        // 检查是否已存在相近位置的节点
                        if (!HasNodeNearPosition(transitionPos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, transitionPos, collider, true, isOneWay));
                        }
                    }

                    // 上层右边缘在下层范围内 → 在下层的 upper.right 位置生成节点
                    if (upper.right > lower.left + config.EdgeInset && upper.right < lower.right - config.EdgeInset)
                    {
                        Vector3 transitionPos = new Vector3(upper.right, lower.y, 0f);
                        if (!HasNodeNearPosition(transitionPos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, transitionPos, collider, false, isOneWay));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 检查指定位置附近是否已存在节点
        /// </summary>
        private bool HasNodeNearPosition(Vector3 position, float threshold)
        {
            float thresholdSq = threshold * threshold;
            foreach (var node in Nodes)
            {
                if ((node.Position - position).sqrMagnitude < thresholdSq)
                {
                    return true;
                }
            }
            return false;
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
            if (_allEdgesCache.Count < 2) return;

            // 按 Y 坐标升序排序
            var sortedByY = new List<(float left, float right, float y, Collider2D collider, bool isOneWay)>(_allEdgesCache);
            sortedByY.Sort((a, b) => a.y.CompareTo(b.y));

            for (int i = 0; i < sortedByY.Count; i++)
            {
                var lower = sortedByY[i];

                for (int j = i + 1; j < sortedByY.Count; j++)
                {
                    var upper = sortedByY[j];

                    float heightDiff = upper.y - lower.y;
                    if (heightDiff > 8f) continue;  // 高度差太大（超过最大跳跃高度）
                    if (heightDiff < 0.5f) continue; // 同一平面

                    // 检查上层平台的边缘是否在下层平台的 X 范围内
                    float inset = config.EdgeInset;

                    // 上层左边缘在下层范围内 → 在下层的 upper.left 位置生成节点
                    if (upper.left > lower.left + inset && upper.left < lower.right - inset)
                    {
                        Vector3 pos = new Vector3(upper.left, lower.y, 0f);
                        if (!HasNodeNearPosition(pos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, pos, lower.collider, true, lower.isOneWay));
                        }
                    }

                    // 上层右边缘在下层范围内 → 在下层的 upper.right 位置生成节点
                    if (upper.right > lower.left + inset && upper.right < lower.right - inset)
                    {
                        Vector3 pos = new Vector3(upper.right, lower.y, 0f);
                        if (!HasNodeNearPosition(pos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, pos, lower.collider, false, lower.isOneWay));
                        }
                    }

                    // === 新增：向上生成落地点 ===
                    // 检查下层平台的边缘是否在上层平台的 X 范围内
                    // 如果是，在上层平台对应位置生成落地点，使垂直跳跃能够成功

                    // 下层左边缘在上层范围内 → 在上层的 lower.left 位置生成落地点
                    if (lower.left > upper.left + inset && lower.left < upper.right - inset)
                    {
                        Vector3 pos = new Vector3(lower.left, upper.y, 0f);
                        if (!HasNodeNearPosition(pos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, pos, upper.collider, true, upper.isOneWay));
                        }
                    }

                    // 下层右边缘在上层范围内 → 在上层的 lower.right 位置生成落地点
                    if (lower.right > upper.left + inset && lower.right < upper.right - inset)
                    {
                        Vector3 pos = new Vector3(lower.right, upper.y, 0f);
                        if (!HasNodeNearPosition(pos, 0.3f))
                        {
                            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, pos, upper.collider, false, upper.isOneWay));
                        }
                    }
                }
            }
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
        private void GenerateNodesForEdge(float left, float right, float y, Collider2D collider, bool isOneWay)
        {
            // 缓存边缘数据，用于后续全局转换节点生成
            _allEdgesCache.Add((left, right, y, collider, isOneWay));

            float width = right - left;
            float nodeSpacing = config.ActualNodeSpacing;

            // 平台太窄，生成一个边缘节点（允许跳跃链接从此节点发起）
            if (width < config.MinPlatformWidth)
            {
                Vector3 centerPos = new Vector3((left + right) / 2f, y, 0f);
                AddNode(PlatformNodeData.CreateEdge(nextNodeId++, centerPos, collider, true, isOneWay));
                return;
            }

            // 生成左边缘节点
            Vector3 leftEdgePos = new Vector3(left + config.EdgeInset, y, 0f);
            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, leftEdgePos, collider, true, isOneWay));

            // 生成右边缘节点
            Vector3 rightEdgePos = new Vector3(right - config.EdgeInset, y, 0f);
            AddNode(PlatformNodeData.CreateEdge(nextNodeId++, rightEdgePos, collider, false, isOneWay));

            // 生成中间表面节点
            float innerWidth = width - 2 * config.EdgeInset;
            int innerNodeCount = Mathf.FloorToInt(innerWidth / nodeSpacing);

            if (innerNodeCount > 0)
            {
                float actualSpacing = innerWidth / (innerNodeCount + 1);
                for (int i = 1; i <= innerNodeCount; i++)
                {
                    float x = left + config.EdgeInset + actualSpacing * i;
                    Vector3 surfacePos = new Vector3(x, y, 0f);
                    AddNode(PlatformNodeData.CreateSurface(nextNodeId++, surfacePos, collider, isOneWay));
                }
            }
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
        }

        /// <summary>
        /// 生成同平台行走链接
        /// </summary>
        private void GenerateWalkLinks()
        {
            const float maxYDiff = 0.5f; // 同一行走平面的最大 Y 坐标差异
            const float maxXGap = 3f; // 相邻节点最大 X 间距（超过则不连接）

            // 按平台 Collider + Y 坐标分组节点
            // Key: (Collider, Y 坐标取整到 0.5)
            var platformGroups = new Dictionary<(Collider2D, int), List<int>>();

            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                if (node.PlatformCollider == null) continue;

                // 将 Y 坐标量化到 0.5 单位，用于分组
                int yGroup = Mathf.RoundToInt(node.Position.y / maxYDiff);
                var key = (node.PlatformCollider, yGroup);

                if (!platformGroups.ContainsKey(key))
                {
                    platformGroups[key] = new List<int>();
                }
                platformGroups[key].Add(i);
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
            // 1. 先在 preferredPlatform 上找最近节点
            if (preferredPlatform != null)
            {
                PlatformNodeData? bestOnPlatform = null;
                float bestDist = maxDistance;

                foreach (var node in Nodes)
                {
                    if (node.PlatformCollider == preferredPlatform)
                    {
                        float dist = Vector2.Distance(position, node.Position);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestOnPlatform = node;
                        }
                    }
                }

                // 如果在指定平台上找到节点，直接返回
                if (bestOnPlatform.HasValue)
                {
                    return bestOnPlatform;
                }
            }

            // 2. 回退到原逻辑：查找任意平台上的最近节点
            return FindNearestNode(position, maxDistance);
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
