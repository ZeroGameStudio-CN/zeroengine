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

        [Tooltip("最大跳跃高度")]
        public float MaxJumpHeight = 6f;

        [Tooltip("重力缩放 (Rigidbody2D.gravityScale)")]
        public float GravityScale = 3f;

        [Header("下落配置")]
        [Tooltip("最大下落高度")]
        public float MaxFallHeight = 10f;

        [Tooltip("最大下落水平距离")]
        public float MaxFallHorizontalDistance = 4f;

        [Tooltip("表面节点垂直下落最大水平距离（小于此值才从表面节点生成下落链接）")]
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
            var obstacleLayer = graphGenerator.Config.ObstacleLayer;

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
                bool isSurfaceNode = fromNode.NodeType == PlatformNodeType.Surface;

                // 计算跳跃到其他平台的链接
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j) continue;

                    var toNode = nodes[j];

                    // 跳过同一平台的节点（改用 Y 坐标差异判断，支持 Tilemap Composite Collider）
                    // 原逻辑用 PlatformCollider 判断，但 Tilemap 所有平台共享一个 Collider，导致跳跃链接无法生成
                    float heightDiff = Mathf.Abs(toNode.Position.y - fromNode.Position.y);
                    if (heightDiff < 0.5f && fromNode.PlatformCollider == toNode.PlatformCollider) continue;

                    // 检查距离限制
                    float horizontalDist = Mathf.Abs(toNode.Position.x - fromNode.Position.x);
                    float verticalDist = toNode.Position.y - fromNode.Position.y;

                    // 目标在上方或同高度 - 尝试跳跃
                    if (verticalDist >= -0.5f && verticalDist <= config.MaxJumpHeight)
                    {
                        // ★ 跳跃链接只从边缘节点发起（防止平台中间多个节点生成重复跳跃链接）
                        if (!isEdgeNode)
                        {
                            jumpSkippedNotEdge++;
                            continue;
                        }

                        // ★ 终点也必须是边缘节点
                        bool toIsEdge = toNode.NodeType == PlatformNodeType.LeftEdge ||
                                        toNode.NodeType == PlatformNodeType.RightEdge;
                        if (!toIsEdge)
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
                            if (TryCreateJumpLink(fromNode, toNode, obstacleLayer, out string failReason))
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
                    else if (verticalDist > config.MaxJumpHeight)
                    {
                        // 仅统计边缘节点的超高度失败，排除 Surface 节点的噪音
                        if (isEdgeNode) jumpFailedHeight++;
                    }
                    // 目标在下方 - 尝试下落（不需要 MinLinkDistance 检查，垂直下落也有效）
                    else if (verticalDist < -0.5f && Mathf.Abs(verticalDist) <= config.MaxFallHeight)
                    {
                        // 边缘节点：完整下落检测（水平 + 垂直）
                        if (isEdgeNode && horizontalDist <= config.MaxFallHorizontalDistance)
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

                            if (TryCreateFallLink(fromNode, toNode, obstacleLayer))
                            {
                                fallLinksCreated++;
                            }
                        }
                        // 表面节点：仅限垂直下落（水平距离很小）
                        // ★ 终点也必须是边缘节点，防止平台内部生成大量无意义的下落链接
                        else if (isSurfaceNode && horizontalDist <= config.SurfaceNodeVerticalFallMaxHorizontal)
                        {
                            bool toIsEdge = toNode.NodeType == PlatformNodeType.LeftEdge ||
                                            toNode.NodeType == PlatformNodeType.RightEdge;
                            if (!toIsEdge)
                            {
                                fallSkippedToNotEdge++;
                                continue;
                            }

                            if (TryCreateFallLink(fromNode, toNode, obstacleLayer))
                            {
                                fallLinksCreated++;
                            }
                        }
                    }
                }

                // 检查穿透单向平台下落（单向平台任意位置都可下穿，不限于边缘节点）
                if (fromNode.IsOneWay)
                {
                    var dropLinks = CreateDropThroughLinks(fromNode, nodes, obstacleLayer);
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
                Debug.Log($"[JumpLinkCalculator] 配置: MaxJumpHeight={config.MaxJumpHeight}, MaxHorizontalDistance={config.MaxHorizontalDistance}, " +
                          $"MaxJumpVelocity={config.MaxJumpVelocity}, ObstacleLayer={obstacleLayer.value}");
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
                config.MaxJumpVelocity,
                config.GravityScale,
                config.Overshoot
            );

            if (!result.IsReachable)
            {
                failReason = "unreachable";
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
                config.GravityScale
            );

            if (!result.IsReachable) return false;

            // 简单的直线碰撞检测
            Vector2 direction = ((Vector2)to.Position - (Vector2)from.Position).normalized;
            float distance = Vector2.Distance(from.Position, to.Position);

            RaycastHit2D hit = Physics2D.CircleCast(
                from.Position,
                config.TrajectoryCheckRadius,
                direction,
                distance,
                obstacleLayer
            );

            if (hit.collider != null && hit.collider != to.PlatformCollider)
            {
                return false;
            }

            // 创建下落链接
            var link = PlatformLinkData.CreateFall(from.NodeId, to.NodeId, result.FlightTime);
            graphGenerator.Links.Add(link);
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
                // 跳过同一平台（使用高度差判断，支持 Tilemap Composite Collider）
                float heightDiff = Mathf.Abs(toNode.Position.y - fromNode.Position.y);
                if (heightDiff < 0.5f && toNode.PlatformCollider == fromNode.PlatformCollider) continue;

                // 目标必须在正下方附近
                float horizontalDist = Mathf.Abs(toNode.Position.x - startPos.x);
                float verticalDist = startPos.y - toNode.Position.y;

                if (horizontalDist > 1f) continue;
                if (verticalDist <= 0.5f || verticalDist > config.MaxFallHeight) continue;

                // 计算下落时间
                var result = JumpMovementHandler.CalculateFall(startPos, toNode.Position, config.GravityScale);

                if (!result.IsReachable) continue;

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
