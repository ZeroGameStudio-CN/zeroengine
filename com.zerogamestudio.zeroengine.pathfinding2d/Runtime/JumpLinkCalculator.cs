// JumpLinkCalculator.cs
// 跳跃链接计算器
// 遍历平台边缘节点，计算可达的跳跃链接

using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 跳跃链接生成配置
    /// </summary>
    [System.Serializable]
    public class JumpLinkConfig
    {
        [Header("跳跃能力")]
        [Tooltip("最大跳跃初速度 (Y)")]
        public float MaxJumpVelocity = 14f;

        [Tooltip("最大水平跳跃距离")]
        public float MaxHorizontalDistance = 6f;

        [Tooltip("最大空中水平速度，0 表示不限制")]
        public float MaxAirHorizontalSpeed = 0f;

        [Tooltip("最大跳跃高度")]
        public float MaxJumpHeight = 6f;

        [Tooltip("重力缩放 (Rigidbody2D.gravityScale)")]
        public float GravityScale = 3f;

        [Tooltip("空中额外跳跃次数，0 表示仅地面起跳")]
        public int AirJumpCount = 0;

        [Tooltip("空中额外跳跃的 Y 初速度，0 表示沿用最大跳跃初速度")]
        public float AirJumpVelocity = 0f;

        [Tooltip("使用按目标高度计算的一次智能跳跃，不继承玩家多段跳")]
        public bool UseSingleSmartJump = false;

        [Tooltip("预留：是否允许墙面穿越建图。当前不参与链接生成。")]
        public bool EnableWallTraversal = false;

        [Header("下落配置")]
        [Tooltip("最大下落高度")]
        public float MaxFallHeight = 10f;

        [Tooltip("最大下落水平距离")]
        public float MaxFallHorizontalDistance = 4f;

        [Tooltip("兼容保留：旧版本用于表面节点垂直下落；当前 Fall 只从真实边缘生成。")]
        public float SurfaceNodeVerticalFallMaxHorizontal = 1.5f;

        [Header("验证参数")]
        [Tooltip("轨迹碰撞检测半径")]
        public float TrajectoryCheckRadius = 0.4f;

        [Tooltip("过冲系数 (1.0 = 刚好到达)")]
        public float Overshoot = 1.2f;

        [Tooltip("最小链接距离 (小于此距离不创建链接)")]
        public float MinLinkDistance = 0.5f;

        [Header("突出平台处理")]
        [Tooltip("启用突出平台绕道跳跃（从边缘节点绕开遮挡）")]
        public bool EnableOverhangBypass = true;

        [Tooltip("检测突出平台的向上射线距离")]
        public float OverhangDetectionHeight = 3f;
    }

    /// <summary>
    /// 跳跃链接计算器
    /// 分析平台图中的边缘节点，生成跳跃和下落链接
    /// </summary>
    public class JumpLinkCalculator : MonoBehaviour
    {
        private const float DefaultGravity = 9.81f;

        [SerializeField]
        private JumpLinkConfig config = new JumpLinkConfig();

        [SerializeField]
        private PlatformGraphGenerator graphGenerator;

        /// <summary>配置</summary>
        public JumpLinkConfig Config => config;

        /// <summary>
        /// 生成所有跳跃链接
        /// </summary>
        public void GenerateJumpLinks()
        {
            if (graphGenerator == null)
            {
                graphGenerator = GetComponent<PlatformGraphGenerator>();
            }

            if (graphGenerator == null || !graphGenerator.IsGenerated)
            {
                Debug.LogWarning("[JumpLinkCalculator] PlatformGraphGenerator 未找到或未生成");
                return;
            }

            var nodes = graphGenerator.Nodes;
            LayerMask obstacleLayer = graphGenerator.Config.ObstacleLayer;
            LayerMask trajectoryBlockerLayer = graphGenerator.Config.GroundLayer | graphGenerator.Config.ObstacleLayer;

            int jumpLinksCreated = 0;
            int fallLinksCreated = 0;
            int dropLinksCreated = 0;

            // 诊断计数器
            int jumpAttempts = 0;
            int jumpFailedDistance = 0;
            int jumpFailedHeight = 0;
            int jumpFailedReachable = 0;
            int jumpFailedTrajectory = 0;
            int jumpSkippedNotEdge = 0;
            int jumpSkippedToNotEdge = 0;
            int fallSkippedToNotEdge = 0;
            int edgeNodeCount = 0;
            float effectiveMaxJumpHeight = GetEffectiveMaxJumpHeight();

            // 预处理：为每个平台找到最近的边缘节点（用于去重）
            // 使用 Y 坐标分组（支持 Tilemap Composite Collider 场景，所有平台共享一个 Collider）
            var platformEdgeCache = BuildPlatformEdgeCacheByHeight(nodes);

            // 统计边缘节点数量
            foreach (var node in nodes)
            {
                if (node.NodeType == PlatformNodeType.LeftEdge || node.NodeType == PlatformNodeType.RightEdge)
                    edgeNodeCount++;
            }

            if (PathfindingLogSettings.EnableDetailedDiagnostics)
            {
                Debug.Log($"[JumpLink诊断] 边缘节点列表:");
                foreach (var node in nodes)
                {
                    if (node.NodeType == PlatformNodeType.LeftEdge || node.NodeType == PlatformNodeType.RightEdge)
                        Debug.Log($"  - {node.NodeType} at {node.Position} (NodeId={node.NodeId}, OneWay={node.IsOneWay})");
                }
            }

            if (PathfindingLogSettings.EnableGenerationSummary)
                Debug.Log($"[JumpLinkCalculator] 节点统计: 总数={nodes.Count}, 边缘节点={edgeNodeCount}, 平台数(按高度)={platformEdgeCache.Count}");

            // 遍历所有节点（跳跃链接仅从边缘节点发起，下落链接根据节点类型区分处理）
            for (int i = 0; i < nodes.Count; i++)
            {
                var fromNode = nodes[i];

                // 判断节点类型
                bool isEdgeNode = fromNode.NodeType == PlatformNodeType.LeftEdge ||
                                  fromNode.NodeType == PlatformNodeType.RightEdge;
                // 计算跳跃到其他平台的链接
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j) continue;

                    var toNode = nodes[j];

                    // 跳过同一连续平台段的节点。Composite/Tilemap 会让多个平台段共享 Collider，
                    // 这里只能用 SurfaceGroupId 判断真实同平台。
                    float heightDiff = Mathf.Abs(toNode.Position.y - fromNode.Position.y);
                    if (fromNode.SurfaceGroupId >= 0 &&
                        fromNode.SurfaceGroupId == toNode.SurfaceGroupId &&
                        heightDiff < 0.5f)
                    {
                        continue;
                    }

                    // 检查距离限制
                    float horizontalDist = Mathf.Abs(toNode.Position.x - fromNode.Position.x);
                    float verticalDist = toNode.Position.y - fromNode.Position.y;

                    // 目标在上方或同高度 - 尝试跳跃
                    if (verticalDist >= -0.5f && verticalDist <= effectiveMaxJumpHeight)
                    {
                        // ★ 跳跃链接只从边缘节点发起（防止平台中间多个节点生成重复跳跃链接）
                        if (!isEdgeNode)
                        {
                            jumpSkippedNotEdge++;
                            continue;
                        }

                        // ★ 常规跳跃终点必须是边缘节点。edge-step-up fallback 额外允许落到
                        // 同一上层 surface 的安全内部点，避免把角色中心导向贴边假落点。
                        bool toIsEdge = toNode.NodeType == PlatformNodeType.LeftEdge ||
                                        toNode.NodeType == PlatformNodeType.RightEdge;
                        if (!toIsEdge && !CanConsiderEdgeStepUpSurfaceTarget(fromNode, toNode, verticalDist))
                        {
                            jumpSkippedToNotEdge++;
                            continue;
                        }

                        // ★ 工业级方案：尝试连接所有可达边缘节点（不只是最近的）
                        // 让轨迹验证决定是否创建链接，而非位置去重
                        // 这样即使最近的边缘被遮挡，也能连接到其他开阔的边缘

                        // 跳跃链接需要最小水平距离（防止原地跳）
                        // 允许垂直跳跃：水平距离小但高度差足够大时不过滤
                        if (horizontalDist < config.MinLinkDistance && Mathf.Abs(verticalDist) < 1f) continue;

                        if (horizontalDist <= config.MaxHorizontalDistance)
                        {
                            jumpAttempts++;
                            if (TryCreateJumpLink(fromNode, toNode, trajectoryBlockerLayer, out string failReason))
                            {
                                jumpLinksCreated++;
                            }
                            else
                            {
                                if (failReason == "unreachable") jumpFailedReachable++;
                                else if (failReason == "trajectory")
                                {
                                    jumpFailedTrajectory++;
                                    // 诊断日志：同一 Collider 但轨迹被阻挡
                                    if (PathfindingLogSettings.EnableDetailedDiagnostics && fromNode.PlatformCollider == toNode.PlatformCollider)
                                    {
                                        Debug.Log($"[JumpLink诊断] 同Collider轨迹阻挡: {fromNode.Position} -> {toNode.Position}, 高度差={verticalDist:F2}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            jumpFailedDistance++;
                        }
                    }
                    else if (verticalDist > effectiveMaxJumpHeight)
                    {
                        // 仅统计边缘节点的超高度失败，排除 Surface 节点的噪音
                        if (isEdgeNode) jumpFailedHeight++;
                    }
                    // 目标在下方 - 尝试下落（不需要 MinLinkDistance 检查，垂直下落也有效）
                    else if (verticalDist < -0.5f && Mathf.Abs(verticalDist) <= config.MaxFallHeight)
                    {
                        // 边缘节点：完整下落检测（水平 + 垂直）
                        if (isEdgeNode && CanStartFallFromEdgeNode(fromNode) && horizontalDist <= config.MaxFallHorizontalDistance)
                        {
                            // ★ 终点也必须是边缘节点
                            bool toIsEdge = toNode.NodeType == PlatformNodeType.LeftEdge ||
                                            toNode.NodeType == PlatformNodeType.RightEdge;
                            if (!toIsEdge)
                            {
                                fallSkippedToNotEdge++;
                                continue;
                            }

                            // ★ 工业级方案：尝试连接所有可达边缘节点（不只是最近的）
                            // 让轨迹验证决定是否创建链接，而非位置去重

                            if (TryCreateFallLink(fromNode, toNode, trajectoryBlockerLayer))
                            {
                                fallLinksCreated++;
                            }
                        }
                    }
                }

                // 检查穿透单向平台下落（单向平台任意位置都可下穿，不限于边缘节点）
                if (fromNode.IsOneWay)
                {
                    var dropLinks = CreateDropThroughLinks(fromNode, nodes, trajectoryBlockerLayer);
                    dropLinksCreated += dropLinks;
                }
            }

            if (PathfindingLogSettings.EnableGenerationSummary)
            {
                Debug.Log($"[JumpLinkCalculator] 链接生成完成: 跳跃 {jumpLinksCreated}, 下落 {fallLinksCreated}, 穿透 {dropLinksCreated}");
            }

            if (PathfindingLogSettings.EnableDetailedDiagnostics)
            {
                Debug.Log($"[JumpLinkCalculator] 跳跃诊断: 尝试={jumpAttempts}, 成功={jumpLinksCreated}, " +
                          $"超距离={jumpFailedDistance}, 超高度={jumpFailedHeight}, 不可达={jumpFailedReachable}, 轨迹阻挡={jumpFailedTrajectory}");
                Debug.Log($"[JumpLinkCalculator] 过滤诊断: 起点非边缘={jumpSkippedNotEdge}, 终点非边缘(跳)={jumpSkippedToNotEdge}, 终点非边缘(落)={fallSkippedToNotEdge}");
                Debug.Log($"[JumpLinkCalculator] 配置: MaxJumpHeight={config.MaxJumpHeight}, EffectiveMaxJumpHeight={effectiveMaxJumpHeight}, MaxHorizontalDistance={config.MaxHorizontalDistance}, " +
                          $"MaxJumpVelocity={config.MaxJumpVelocity}, MaxAirHorizontalSpeed={config.MaxAirHorizontalSpeed}, " +
                          $"ObstacleLayer={obstacleLayer.value}, TrajectoryBlockerLayer={trajectoryBlockerLayer.value}");
            }

            // 构建邻接表，优化 A* 寻路性能（O(n) -> O(1)）
            graphGenerator.BuildAdjacencyList();

            if (PathfindingLogSettings.EnableGenerationSummary)
                Debug.Log($"[JumpLinkCalculator] 邻接表构建完成，共 {graphGenerator.AdjacencyList.Count} 个节点");
        }

        /// <summary>
        /// 构建平台边缘节点缓存（按高度分组，支持 Tilemap Composite Collider）
        /// </summary>
        private Dictionary<int, List<PlatformNodeData>> BuildPlatformEdgeCacheByHeight(List<PlatformNodeData> nodes)
        {
            var cache = new Dictionary<int, List<PlatformNodeData>>();

            foreach (var node in nodes)
            {
                if (node.NodeType != PlatformNodeType.LeftEdge && node.NodeType != PlatformNodeType.RightEdge)
                    continue;

                // 使用 Y 坐标作为平台分组键（0.5 精度）
                int heightKey = Mathf.RoundToInt(node.Position.y * 2);

                if (!cache.ContainsKey(heightKey))
                {
                    cache[heightKey] = new List<PlatformNodeData>();
                }
                cache[heightKey].Add(node);
            }

            return cache;
        }

        /// <summary>
        /// 查找指定高度平台上距离起点最近的边缘节点
        /// </summary>
        private PlatformNodeData? FindNearestEdgeByHeight(
            PlatformNodeData fromNode,
            int targetHeightKey,
            Dictionary<int, List<PlatformNodeData>> edgeCache)
        {
            if (!edgeCache.TryGetValue(targetHeightKey, out var edges) || edges.Count == 0)
                return null;

            PlatformNodeData? nearest = null;
            float minDist = float.MaxValue;

            foreach (var edge in edges)
            {
                float dist = Vector2.Distance(fromNode.Position, edge.Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = edge;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 尝试创建跳跃链接
        /// </summary>
        private bool TryCreateJumpLink(PlatformNodeData from, PlatformNodeData to, LayerMask obstacleLayer, out string failReason)
        {
            failReason = null;

            // 注意：不在链接生成阶段阻止跳跃链接
            // 即使起点头顶有突出平台遮挡，也应该生成链接
            // 让 A* 寻路系统自然选择"先走到边缘再跳跃"的路径
            // 运行时执行阶段由 SummonComponent.Movement.ShouldJump() 检测撞头

            var result = JumpMovementHandler.CalculateJump(
                from.Position,
                to.Position,
                GetEffectiveMaxJumpVelocity(),
                config.GravityScale,
                config.Overshoot,
                config.MaxAirHorizontalSpeed
            );

            if (!result.IsReachable)
            {
                failReason = "unreachable";
                return false;
            }

            bool edgeStepUpCandidate = IsEdgeStepUpCandidate(from, to);
            bool canCreateEdgeJumpLink = edgeStepUpCandidate && CanCreateEdgeJumpLink(from, to);
            if (edgeStepUpCandidate && !canCreateEdgeJumpLink)
            {
                failReason = "unsafe-edge-step-up";
                return false;
            }

            // 验证轨迹无障碍（排除起点和终点平台）
            if (!JumpMovementHandler.ValidateTrajectory(
                result.Trajectory,
                obstacleLayer,
                config.TrajectoryCheckRadius,
                from.PlatformCollider,
                to.PlatformCollider))
            {
                // 边对边"上台阶"兜底：两段平台在同一 X 边界垂直相邻（floor 右缘紧贴上方平台左缘）时，
                // 连接它们的台阶/墙面落在跳跃轨迹里，严格检测会误杀，但玩家完全可以贴着墙跳上这级台阶。
                // 镜像下落的 CanCreateEdgeFallLink：仅当几何是干净的相邻边上跳（起点在本段边缘、目标是
                // 出口正上方第一段平台、在跳跃高度内）才放行。修复 navtest 016：floor→出口平台 Δ7 的
                // 合法上跳被丢，而反向下落链接却存在。
                if (!canCreateEdgeJumpLink)
                {
                    failReason = "trajectory";
                    return false;
                }
            }

            if (edgeStepUpCandidate && HasSameColliderStructuralTrajectoryBlocker(result.Trajectory, from, to))
            {
                failReason = "trajectory";
                return false;
            }

            // 创建跳跃链接（包含预计算的轨迹点用于可视化）
            var link = PlatformLinkData.CreateJump(
                from.NodeId,
                to.NodeId,
                result.VelocityY,
                result.VelocityX,
                result.FlightTime,
                result.Trajectory
            );

            graphGenerator.Links.Add(link);
            return true;
        }

        private bool HasSameColliderStructuralTrajectoryBlocker(
            Vector2[] trajectory,
            PlatformNodeData from,
            PlatformNodeData to)
        {
            if (graphGenerator == null ||
                trajectory == null ||
                trajectory.Length < 2 ||
                from.PlatformCollider == null ||
                from.PlatformCollider != to.PlatformCollider ||
                from.SurfaceGroupId < 0 ||
                to.SurfaceGroupId < 0 ||
                from.SurfaceGroupId == to.SurfaceGroupId)
            {
                return false;
            }

            var sharedCollider = from.PlatformCollider;
            if (!graphGenerator.TryGetSurfaceSegment(from.SurfaceGroupId, out var fromSegment) ||
                !graphGenerator.TryGetSurfaceSegment(to.SurfaceGroupId, out var toSegment))
            {
                return false;
            }

            LayerMask blockerMask = graphGenerator.Config.GroundLayer | graphGenerator.Config.ObstacleLayer;
            float radius = Mathf.Max(0.01f, config.TrajectoryCheckRadius);
            float endpointIgnoreDistance = radius + 0.05f;
            Vector2 start = trajectory[0];
            Vector2 end = trajectory[trajectory.Length - 1];

            for (int i = 0; i < trajectory.Length - 1; i++)
            {
                Vector2 segmentStart = trajectory[i];
                Vector2 segmentEnd = trajectory[i + 1];
                float segmentDistance = Vector2.Distance(segmentStart, segmentEnd);
                if (segmentDistance <= Mathf.Epsilon)
                    continue;

                Vector2 direction = (segmentEnd - segmentStart).normalized;
                RaycastHit2D[] hits = Physics2D.CircleCastAll(
                    segmentStart,
                    radius,
                    direction,
                    segmentDistance,
                    blockerMask);

                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    var hit = hits[hitIndex];
                    if (hit.collider != sharedCollider)
                        continue;

                    if (IsEndpointTrajectoryContact(hit.point, hit.centroid, start, end, endpointIgnoreDistance))
                        continue;

                    if (IsExpectedSurfaceContact(hit.point, hit.centroid, fromSegment, toSegment, radius))
                        continue;

                    return true;
                }

                int steps = Mathf.Max(1, Mathf.CeilToInt(segmentDistance / Mathf.Max(0.1f, radius)));
                for (int step = 0; step <= steps; step++)
                {
                    Vector2 sample = Vector2.Lerp(segmentStart, segmentEnd, (float)step / steps);
                    Collider2D[] overlaps = Physics2D.OverlapCircleAll(sample, radius, blockerMask);
                    for (int overlapIndex = 0; overlapIndex < overlaps.Length; overlapIndex++)
                    {
                        if (overlaps[overlapIndex] != sharedCollider)
                            continue;

                        if (IsEndpointTrajectoryContact(sample, sample, start, end, endpointIgnoreDistance))
                            continue;

                        if (IsExpectedSurfaceContact(sample, sample, fromSegment, toSegment, radius))
                            continue;

                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsEndpointTrajectoryContact(
            Vector2 hitPoint,
            Vector2 centroid,
            Vector2 start,
            Vector2 end,
            float endpointIgnoreDistance)
        {
            return Vector2.Distance(hitPoint, start) <= endpointIgnoreDistance ||
                   Vector2.Distance(hitPoint, end) <= endpointIgnoreDistance ||
                   Vector2.Distance(centroid, start) <= endpointIgnoreDistance ||
                   Vector2.Distance(centroid, end) <= endpointIgnoreDistance;
        }

        private static bool IsExpectedSurfaceContact(
            Vector2 hitPoint,
            Vector2 centroid,
            PlatformSurfaceSegment fromSegment,
            PlatformSurfaceSegment toSegment,
            float radius)
        {
            return IsNearSurfaceTop(hitPoint, fromSegment, radius) ||
                   IsNearSurfaceTop(centroid, fromSegment, radius) ||
                   IsNearSurfaceTop(hitPoint, toSegment, radius) ||
                   IsNearSurfaceTop(centroid, toSegment, radius);
        }

        private static bool IsNearSurfaceTop(
            Vector2 point,
            PlatformSurfaceSegment segment,
            float radius)
        {
            if (segment == null)
                return false;

            float tolerance = radius + 0.05f;
            return segment.ContainsX(point.x, tolerance) &&
                   point.y >= segment.Y - tolerance &&
                   point.y <= segment.Y + tolerance;
        }

        private float GetEffectiveMaxJumpHeight()
        {
            if (config.UseSingleSmartJump)
                return Mathf.Max(0f, config.MaxJumpHeight);

            int airJumpCount = Mathf.Max(0, config.AirJumpCount);
            if (airJumpCount == 0)
                return Mathf.Max(0f, config.MaxJumpHeight);

            float gravity = DefaultGravity * Mathf.Max(0.01f, config.GravityScale);
            float groundJumpHeight = config.MaxJumpVelocity * config.MaxJumpVelocity / (2f * gravity);
            float airJumpVelocity = config.AirJumpVelocity > 0f
                ? config.AirJumpVelocity
                : config.MaxJumpVelocity;
            float airJumpHeight = airJumpVelocity * airJumpVelocity / (2f * gravity);
            float profileJumpHeight = groundJumpHeight + airJumpCount * airJumpHeight;
            return Mathf.Max(config.MaxJumpHeight, profileJumpHeight);
        }

        private float GetEffectiveMaxJumpVelocity()
        {
            if (config.UseSingleSmartJump || config.AirJumpCount > 0)
            {
                float gravity = DefaultGravity * Mathf.Max(0.01f, config.GravityScale);
                return Mathf.Sqrt(2f * gravity * GetEffectiveMaxJumpHeight());
            }

            return config.MaxJumpVelocity;
        }

        /// <summary>
        /// 检测节点头顶是否有突出平台遮挡
        /// </summary>
        /// <param name="position">检测位置</param>
        /// <param name="platformLayers">平台层</param>
        /// <returns>遮挡的碰撞体，无遮挡返回 null</returns>
        private Collider2D DetectOverhangAbove(Vector2 position, LayerMask platformLayers)
        {
            RaycastHit2D hit = Physics2D.Raycast(
                position + Vector2.up * 0.5f,
                Vector2.up,
                config.OverhangDetectionHeight,
                platformLayers
            );
            return hit.collider;
        }

        /// <summary>
        /// 为被突出平台遮挡的节点查找可用的边缘跳跃点
        /// 返回该平台上最近的、头顶无遮挡的边缘节点
        /// </summary>
        private PlatformNodeData? FindClearEdgeNode(PlatformNodeData blockedNode, List<PlatformNodeData> allNodes, LayerMask platformLayers)
        {
            PlatformNodeData? bestEdge = null;
            float bestDist = float.MaxValue;

            foreach (var node in allNodes)
            {
                // 必须是同一平台的边缘节点
                if (node.PlatformCollider != blockedNode.PlatformCollider) continue;
                if (node.NodeType != PlatformNodeType.LeftEdge && node.NodeType != PlatformNodeType.RightEdge) continue;

                // 检查该边缘节点头顶是否无遮挡
                var overhang = DetectOverhangAbove(node.Position, platformLayers);
                if (overhang != null) continue;

                // 选择最近的边缘节点
                float dist = Vector2.Distance(blockedNode.Position, node.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestEdge = node;
                }
            }

            return bestEdge;
        }

        /// <summary>
        /// 尝试创建下落链接
        /// </summary>
        private bool TryCreateFallLink(PlatformNodeData from, PlatformNodeData to, LayerMask obstacleLayer)
        {
            var result = JumpMovementHandler.CalculateFall(
                from.Position,
                to.Position,
                config.GravityScale,
                config.MaxAirHorizontalSpeed
            );

            if (!result.IsReachable) return false;

            if (!JumpMovementHandler.ValidateTrajectory(
                    result.Trajectory,
                    obstacleLayer,
                    config.TrajectoryCheckRadius,
                    from.PlatformCollider,
                    to.PlatformCollider))
            {
                if (!CanCreateEdgeFallLink(from, to))
                    return false;
            }

            // 创建下落链接
            var link = PlatformLinkData.CreateFall(from.NodeId, to.NodeId, result.FlightTime);
            graphGenerator.Links.Add(link);
            return true;
        }

        private bool CanStartFallFromEdgeNode(PlatformNodeData node)
        {
            if (!node.IsTransitionAnchor)
                return true;

            if (graphGenerator == null ||
                node.SurfaceGroupId < 0 ||
                !graphGenerator.TryGetSurfaceSegment(node.SurfaceGroupId, out var segment))
            {
                return false;
            }

            float edgeTolerance = Mathf.Max(graphGenerator.Config.EdgeInset + 0.1f, 0.25f);
            return node.NodeType switch
            {
                PlatformNodeType.LeftEdge => Mathf.Abs(node.Position.x - segment.MinX) <= edgeTolerance,
                PlatformNodeType.RightEdge => Mathf.Abs(node.Position.x - segment.MaxX) <= edgeTolerance,
                _ => false
            };
        }

        private bool CanCreateEdgeFallLink(PlatformNodeData from, PlatformNodeData to)
        {
            if (graphGenerator == null ||
                from.PlatformCollider == null ||
                to.PlatformCollider == null ||
                from.SurfaceGroupId < 0 ||
                to.SurfaceGroupId < 0 ||
                from.SurfaceGroupId == to.SurfaceGroupId)
            {
                return false;
            }

            if (to.Position.y >= from.Position.y - 0.5f)
                return false;

            if (!graphGenerator.TryGetSurfaceSegment(from.SurfaceGroupId, out var fromSegment) ||
                !graphGenerator.TryGetSurfaceSegment(to.SurfaceGroupId, out var toSegment))
            {
                return false;
            }

            float edgeInset = Mathf.Max(0.05f, graphGenerator.Config.EdgeInset);
            float edgeTolerance = edgeInset + 0.1f;
            float exitOffset = Mathf.Max(0.05f, edgeInset * 0.5f);
            float landingTolerance = Mathf.Max(edgeInset + 0.05f, 0.25f);

            if (from.NodeType == PlatformNodeType.RightEdge)
            {
                if (Mathf.Abs(from.Position.x - fromSegment.MaxX) > edgeTolerance)
                    return false;

                float exitX = fromSegment.MaxX + exitOffset;
                return toSegment.ContainsX(exitX, landingTolerance) &&
                       to.Position.x >= from.Position.x - landingTolerance &&
                       IsFirstLandingBelowEdge(fromSegment, toSegment, exitX, landingTolerance);
            }

            if (from.NodeType == PlatformNodeType.LeftEdge)
            {
                if (Mathf.Abs(from.Position.x - fromSegment.MinX) > edgeTolerance)
                    return false;

                float exitX = fromSegment.MinX - exitOffset;
                return toSegment.ContainsX(exitX, landingTolerance) &&
                       to.Position.x <= from.Position.x + landingTolerance &&
                       IsFirstLandingBelowEdge(fromSegment, toSegment, exitX, landingTolerance);
            }

            return false;
        }

        // Upward mirror of CanCreateEdgeFallLink: allow a near-vertical "step-up" jump between two
        // vertically-adjacent platform edges (from at its segment edge, target the first surface directly
        // above the exit point, within the jump envelope) even when ValidateTrajectory rejects it because the
        // connecting step/wall lies in the arc. A player can jump up beside that wall onto the ledge.
        private bool CanCreateEdgeJumpLink(PlatformNodeData from, PlatformNodeData to)
        {
            if (graphGenerator == null ||
                from.PlatformCollider == null ||
                to.PlatformCollider == null ||
                from.SurfaceGroupId < 0 ||
                to.SurfaceGroupId < 0 ||
                from.SurfaceGroupId == to.SurfaceGroupId)
            {
                return false;
            }

            float rise = to.Position.y - from.Position.y;
            if (rise <= 0.5f || rise > GetEffectiveMaxJumpHeight())
            {
                return false;
            }

            if (!graphGenerator.TryGetSurfaceSegment(from.SurfaceGroupId, out var fromSegment) ||
                !graphGenerator.TryGetSurfaceSegment(to.SurfaceGroupId, out var toSegment))
            {
                return false;
            }

            float edgeInset = Mathf.Max(0.05f, graphGenerator.Config.EdgeInset);
            float edgeTolerance = edgeInset + 0.1f;
            float exitOffset = Mathf.Max(0.05f, edgeInset * 0.5f);
            float landingTolerance = Mathf.Max(edgeInset + 0.05f, 0.25f);

            if (from.NodeType == PlatformNodeType.RightEdge)
            {
                if (Mathf.Abs(from.Position.x - fromSegment.MaxX) > edgeTolerance)
                {
                    return false;
                }

                float exitX = fromSegment.MaxX + exitOffset;
                return toSegment.ContainsX(exitX, landingTolerance) &&
                       to.Position.x >= from.Position.x - landingTolerance &&
                       IsNearEdgeStepUpLanding(from, to, fromSegment) &&
                       HasSafeLandingCenter(to.Position.x, toSegment) &&
                       IsFirstSurfaceAboveEdge(fromSegment, toSegment, exitX, landingTolerance) &&
                       HasLandingHeadClearance(to.Position.x, toSegment);
            }

            if (from.NodeType == PlatformNodeType.LeftEdge)
            {
                if (Mathf.Abs(from.Position.x - fromSegment.MinX) > edgeTolerance)
                    return false;

                float exitX = fromSegment.MinX - exitOffset;
                return toSegment.ContainsX(exitX, landingTolerance) &&
                       to.Position.x <= from.Position.x + landingTolerance &&
                       IsNearEdgeStepUpLanding(from, to, fromSegment) &&
                       HasSafeLandingCenter(to.Position.x, toSegment) &&
                       IsFirstSurfaceAboveEdge(fromSegment, toSegment, exitX, landingTolerance) &&
                       HasLandingHeadClearance(to.Position.x, toSegment);
            }

            return false;
        }

        private bool IsEdgeStepUpCandidate(PlatformNodeData from, PlatformNodeData to)
        {
            if (graphGenerator == null ||
                from.SurfaceGroupId < 0 ||
                to.SurfaceGroupId < 0 ||
                from.SurfaceGroupId == to.SurfaceGroupId)
            {
                return false;
            }

            if (from.NodeType != PlatformNodeType.LeftEdge &&
                from.NodeType != PlatformNodeType.RightEdge)
            {
                return false;
            }

            float rise = to.Position.y - from.Position.y;
            if (rise <= 0.5f || rise > GetEffectiveMaxJumpHeight())
                return false;

            if (!graphGenerator.TryGetSurfaceSegment(from.SurfaceGroupId, out var fromSegment) ||
                !graphGenerator.TryGetSurfaceSegment(to.SurfaceGroupId, out var toSegment))
            {
                return false;
            }

            float edgeInset = Mathf.Max(0.05f, graphGenerator.Config.EdgeInset);
            float edgeTolerance = edgeInset + 0.1f;
            float exitOffset = Mathf.Max(0.05f, edgeInset * 0.5f);
            float landingTolerance = Mathf.Max(edgeInset + 0.05f, 0.25f);

            if (from.NodeType == PlatformNodeType.RightEdge)
            {
                if (Mathf.Abs(from.Position.x - fromSegment.MaxX) > edgeTolerance)
                    return false;

                float exitX = fromSegment.MaxX + exitOffset;
                return toSegment.ContainsX(exitX, landingTolerance) &&
                       IsFirstSurfaceAboveEdge(fromSegment, toSegment, exitX, landingTolerance);
            }

            if (Mathf.Abs(from.Position.x - fromSegment.MinX) > edgeTolerance)
                return false;

            float leftExitX = fromSegment.MinX - exitOffset;
            return toSegment.ContainsX(leftExitX, landingTolerance) &&
                   IsFirstSurfaceAboveEdge(fromSegment, toSegment, leftExitX, landingTolerance);
        }

        private bool CanConsiderEdgeStepUpSurfaceTarget(
            PlatformNodeData from,
            PlatformNodeData to,
            float verticalDist)
        {
            if (to.NodeType != PlatformNodeType.Surface || verticalDist <= 0.5f)
                return false;

            return CanCreateEdgeJumpLink(from, to);
        }

        private bool IsNearEdgeStepUpLanding(
            PlatformNodeData from,
            PlatformNodeData to,
            PlatformSurfaceSegment fromSegment)
        {
            if (to.NodeType != PlatformNodeType.Surface)
                return true;

            var graphConfig = graphGenerator.Config;
            float safeInset = Mathf.Max(
                Mathf.Max(Mathf.Max(graphConfig.CharacterRadius, graphConfig.EdgeInset), config.TrajectoryCheckRadius) + 0.15f,
                graphConfig.EdgeInset + 0.15f);
            float surfaceSlack = 0.15f;
            float maxSurfaceOffset = safeInset + surfaceSlack;

            if (from.NodeType == PlatformNodeType.RightEdge)
                return to.Position.x <= fromSegment.MaxX + maxSurfaceOffset;

            if (from.NodeType == PlatformNodeType.LeftEdge)
                return to.Position.x >= fromSegment.MinX - maxSurfaceOffset;

            return false;
        }

        private bool HasSafeLandingCenter(float landingX, PlatformSurfaceSegment landingSegment)
        {
            if (graphGenerator == null || landingSegment == null)
                return false;

            float horizontalClearance = Mathf.Max(
                graphGenerator.Config.CharacterRadius,
                config.TrajectoryCheckRadius) + 0.05f;

            return landingX >= landingSegment.MinX + horizontalClearance &&
                   landingX <= landingSegment.MaxX - horizontalClearance;
        }

        private bool HasLandingHeadClearance(float landingX, PlatformSurfaceSegment landingSegment)
        {
            if (graphGenerator == null || landingSegment == null)
                return true;

            var graphConfig = graphGenerator.Config;
            float characterHeight = Mathf.Max(0.05f, graphConfig.CharacterHeight);
            float clearanceBottom = landingSegment.Y + 0.05f;
            float clearanceTop = clearanceBottom + characterHeight;
            float clearanceWidth = Mathf.Max(
                graphConfig.CharacterRadius * 2f,
                config.TrajectoryCheckRadius * 2f);
            Vector2 center = new Vector2(landingX, clearanceBottom + characterHeight * 0.5f);
            Vector2 size = new Vector2(clearanceWidth, characterHeight);
            LayerMask blockerMask = graphConfig.GroundLayer | graphConfig.ObstacleLayer;

            var blockers = Physics2D.OverlapBoxAll(center, size, 0f, blockerMask);
            for (int i = 0; i < blockers.Length; i++)
            {
                var blocker = blockers[i];
                if (blocker == null)
                    continue;

                if (blocker == landingSegment.Collider &&
                    !HasSameColliderSurfaceInsideClearance(blocker, landingSegment, landingX, clearanceBottom, clearanceTop))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private bool HasSameColliderSurfaceInsideClearance(
            Collider2D collider,
            PlatformSurfaceSegment landingSegment,
            float landingX,
            float clearanceBottom,
            float clearanceTop)
        {
            if (graphGenerator?.SurfaceSegments == null)
                return false;

            float xTolerance = Mathf.Max(0.05f, graphGenerator.Config.CharacterRadius);
            for (int i = 0; i < graphGenerator.SurfaceSegments.Count; i++)
            {
                var segment = graphGenerator.SurfaceSegments[i];
                if (segment == null ||
                    segment == landingSegment ||
                    segment.Collider != collider ||
                    !segment.ContainsX(landingX, xTolerance))
                {
                    continue;
                }

                if (segment.Y > clearanceBottom && segment.Y <= clearanceTop)
                    return true;
            }

            return false;
        }

        // True when toSegment is the FIRST walkable surface directly above the exit point (no intermediate
        // platform between from and to at exitX) — so the step-up jump lands on `to`, not something between.
        private bool IsFirstSurfaceAboveEdge(
            PlatformSurfaceSegment fromSegment,
            PlatformSurfaceSegment toSegment,
            float exitX,
            float landingTolerance)
        {
            if (graphGenerator?.SurfaceSegments == null)
                return true;

            float verticalTolerance = Mathf.Max(0.05f, landingTolerance * 0.1f);
            foreach (var segment in graphGenerator.SurfaceSegments)
            {
                if (segment == null ||
                    segment.GroupId == fromSegment.GroupId ||
                    segment.GroupId == toSegment.GroupId ||
                    !segment.ContainsX(exitX, landingTolerance))
                {
                    continue;
                }

                bool aboveStart = segment.Y > fromSegment.Y + verticalTolerance;
                bool belowTarget = segment.Y < toSegment.Y - verticalTolerance;
                if (aboveStart && belowTarget)
                    return false;
            }

            return true;
        }

        private bool IsFirstLandingBelowEdge(
            PlatformSurfaceSegment fromSegment,
            PlatformSurfaceSegment toSegment,
            float exitX,
            float landingTolerance)
        {
            if (graphGenerator?.SurfaceSegments == null)
                return false;

            float verticalTolerance = Mathf.Max(0.05f, landingTolerance * 0.1f);
            foreach (var segment in graphGenerator.SurfaceSegments)
            {
                if (segment == null ||
                    segment.GroupId == fromSegment.GroupId ||
                    segment.GroupId == toSegment.GroupId ||
                    !segment.ContainsX(exitX, landingTolerance))
                {
                    continue;
                }

                bool belowStart = segment.Y < fromSegment.Y - verticalTolerance;
                bool aboveTarget = segment.Y > toSegment.Y + verticalTolerance;
                if (belowStart && aboveTarget)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 创建穿透单向平台的下落链接
        /// </summary>
        private int CreateDropThroughLinks(PlatformNodeData fromNode, List<PlatformNodeData> allNodes, LayerMask obstacleLayer)
        {
            int created = 0;

            // 向下检测可以穿透到达的平台
            Vector2 startPos = fromNode.Position;

            foreach (var toNode in allNodes)
            {
                // 跳过同一连续平台段。
                float heightDiff = Mathf.Abs(toNode.Position.y - fromNode.Position.y);
                if (fromNode.SurfaceGroupId >= 0 &&
                    fromNode.SurfaceGroupId == toNode.SurfaceGroupId &&
                    heightDiff < 0.5f)
                {
                    continue;
                }

                // 目标必须在正下方附近
                float horizontalDist = Mathf.Abs(toNode.Position.x - startPos.x);
                float verticalDist = startPos.y - toNode.Position.y;

                if (horizontalDist > 1f) continue;
                if (verticalDist <= 0.5f || verticalDist > config.MaxFallHeight) continue;

                // 计算下落时间
                var result = JumpMovementHandler.CalculateFall(
                    startPos,
                    toNode.Position,
                    config.GravityScale,
                    config.MaxAirHorizontalSpeed);

                if (!result.IsReachable) continue;

                if (!JumpMovementHandler.ValidateTrajectory(
                        result.Trajectory,
                        obstacleLayer,
                        config.TrajectoryCheckRadius,
                        fromNode.PlatformCollider,
                        toNode.PlatformCollider))
                {
                    continue;
                }

                // 创建穿透下落链接
                var link = PlatformLinkData.CreateDropThrough(fromNode.NodeId, toNode.NodeId, result.FlightTime);
                graphGenerator.Links.Add(link);
                created++;
            }

            return created;
        }

        /// <summary>
        /// 清除所有跳跃链接（保留行走链接）
        /// </summary>
        public void ClearJumpLinks()
        {
            if (graphGenerator == null) return;

            graphGenerator.Links.RemoveAll(link =>
                link.LinkType == PlatformLinkType.Jump ||
                link.LinkType == PlatformLinkType.Fall ||
                link.LinkType == PlatformLinkType.DropThrough
            );
        }

        /// <summary>
        /// 重新生成跳跃链接
        /// </summary>
        public void RegenerateJumpLinks()
        {
            ClearJumpLinks();
            GenerateJumpLinks();
        }

        /// <summary>
        /// 验证物理配置是否与角色 Rigidbody2D 匹配
        /// </summary>
        /// <param name="characterRb">角色的 Rigidbody2D</param>
        /// <returns>是否匹配</returns>
        public bool ValidatePhysicsConfig(Rigidbody2D characterRb)
        {
            if (characterRb == null) return false;

            if (Mathf.Abs(characterRb.gravityScale - config.GravityScale) > 0.1f)
            {
                Debug.LogWarning($"[JumpLinkCalculator] GravityScale 不匹配! " +
                    $"配置: {config.GravityScale}, 实际: {characterRb.gravityScale}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 同步物理配置到角色 Rigidbody2D 的实际值
        /// </summary>
        /// <param name="characterRb">角色的 Rigidbody2D</param>
        public void SyncPhysicsConfig(Rigidbody2D characterRb)
        {
            if (characterRb == null) return;
            config.GravityScale = characterRb.gravityScale;
        }

#if UNITY_EDITOR
        [ContextMenu("生成跳跃链接")]
        private void EditorGenerateJumpLinks()
        {
            if (graphGenerator == null)
            {
                graphGenerator = GetComponent<PlatformGraphGenerator>();
            }

            if (graphGenerator != null && !graphGenerator.IsGenerated)
            {
                graphGenerator.GeneratePlatformGraph();
            }

            GenerateJumpLinks();
        }

        [ContextMenu("清除跳跃链接")]
        private void EditorClearJumpLinks()
        {
            ClearJumpLinks();
        }
#endif
    }
}
