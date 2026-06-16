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

        /// <summary>当前路径</summary>
        public Platform2DPath CurrentPath { get; private set; }

        /// <summary>是否有有效路径</summary>
        public bool HasValidPath => CurrentPath != null &&
                                    CurrentPath.Status == PathStatus.Valid &&
                                    !CurrentPath.IsComplete;

        private float lastPathRequestTime;

        private void Awake()
        {
            if (graphGenerator == null)
            {
                graphGenerator = FindFirstObjectByType<PlatformGraphGenerator>();
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
            // 检查请求间隔
            if (!forceRequest && Time.time - lastPathRequestTime < config.PathRequestInterval)
            {
                return false;
            }

            lastPathRequestTime = Time.time;

            // 检查图是否已生成
            if (graphGenerator == null || !graphGenerator.IsGenerated)
            {
                CurrentPath = Platform2DPath.NotFound(start, end);
                return false;
            }

            // 检查是否已经到达目标
            if (Vector2.Distance(start, end) <= config.ArriveDistance)
            {
                CurrentPath = new Platform2DPath(start, end, new List<MoveCommand>());
                return true;
            }

            // 尝试同平台快速路径（跳过 A*）
            var samePlatformPath = TryCreateSamePlatformPath(start, end);
            if (samePlatformPath != null)
            {
                CurrentPath = samePlatformPath;
                return true;
            }

            // 查找起点和终点节点
            // 优先选择脚下平台上的节点，避免错误选择头顶平台节点
            var startPlatform = DetectPlatformBelow(start);
            var endPlatform = DetectPlatformBelow(end);
            var startNode = graphGenerator.FindNearestNodeOnPlatform(start, startPlatform, config.MaxNodeSearchRadius);
            var endNode = graphGenerator.FindNearestNodeOnPlatform(end, endPlatform, config.MaxNodeSearchRadius);

            if (!startNode.HasValue || !endNode.HasValue)
            {
                // 找不到节点，尝试回退策略
                if (config.AllowPartialPath)
                {
                    var partialPath = TryCreatePartialPath(start, end, startNode, endNode);
                    if (partialPath != null)
                    {
                        CurrentPath = partialPath;
                        return true;
                    }
                }
                CurrentPath = Platform2DPath.NotFound(start, end);
                return false;
            }

            // 执行 A* 寻路
            var path = FindPath(startNode.Value, endNode.Value, start, end);

            // 如果找不到路径，尝试回退策略
            if (path.Status == PathStatus.NotFound && config.AllowPartialPath)
            {
                var partialPath = TryFindPartialPath(startNode.Value, endNode.Value, start, end);
                if (partialPath != null)
                {
                    path = partialPath;
                }
            }

            CurrentPath = path;
            return path.Status == PathStatus.Valid;
        }

        /// <summary>
        /// 尝试创建同平台快速路径（直接行走，跳过 A*）
        /// </summary>
        private Platform2DPath TryCreateSamePlatformPath(Vector3 start, Vector3 end)
        {
            // 检测起点和终点脚下的平台
            var startPlatform = DetectPlatformBelow(start);
            var endPlatform = DetectPlatformBelow(end);

            // 如果不在同一平台，返回 null
            if (startPlatform == null || endPlatform == null || startPlatform != endPlatform)
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
            // 如果只是终点找不到节点，走到最近的可达位置
            if (startNode.HasValue && !endNode.HasValue)
            {
                // 找到离终点最近的节点作为临时目标
                var nearestToEnd = graphGenerator.FindNearestNode(end, float.MaxValue);
                if (nearestToEnd.HasValue)
                {
                    return FindPath(startNode.Value, nearestToEnd.Value, start, nearestToEnd.Value.Position);
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
            // 找到离终点最近的可达节点
            var closestReachable = FindClosestReachableNode(startNode, endNode);

            if (closestReachable.HasValue && closestReachable.Value.NodeId != startNode.NodeId)
            {
                return FindPath(startNode, closestReachable.Value, actualStart, closestReachable.Value.Position);
            }

            return null;
        }

        /// <summary>
        /// 找到从起点可达的、离终点最近的节点
        /// </summary>
        private PlatformNodeData? FindClosestReachableNode(PlatformNodeData startNode, PlatformNodeData endNode)
        {
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
                // 计算到当前目标点的距离
                float deviation = Vector2.Distance(currentPosition, currentCmd.Value.Target);

                // 如果偏离太远且不是在执行跳跃/下落
                if (deviation > config.PathDeviationThreshold &&
                    currentCmd.Value.CommandType == MoveCommandType.Walk)
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
                    // 行走：到达目标 X 坐标附近
                    return Mathf.Abs(currentPosition.x - command.Target.x) < config.ArriveDistance;

                case MoveCommandType.Jump:
                case MoveCommandType.Fall:
                case MoveCommandType.DropDown:
                    // 跳跃/下落：落地且接近目标位置
                    if (!isGrounded) return false;
                    return Vector2.Distance(currentPosition, command.Target) < config.ArriveDistance * 2f;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 清除当前路径
        /// </summary>
        public void ClearPath()
        {
            CurrentPath = null;
        }

        /// <summary>
        /// A* 寻路实现
        /// </summary>
        private Platform2DPath FindPath(PlatformNodeData startNode, PlatformNodeData endNode, Vector3 actualStart, Vector3 actualEnd)
        {
            var nodes = graphGenerator.Nodes;
            var nodeCount = nodes.Count;

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
                    return ReconstructPath(cameFrom, cameFromLink, current, actualStart, actualEnd);
                }

                openSet.Remove(current);
                closedSet.Add(current);

                // 遍历所有出边
                var outgoingLinks = graphGenerator.GetOutgoingLinks(current);

                foreach (var link in outgoingLinks)
                {
                    if (closedSet.Contains(link.ToNodeId)) continue;

                    float tentativeG = gScore[current] + link.Cost;

                    if (!gScore.ContainsKey(link.ToNodeId) || tentativeG < gScore[link.ToNodeId])
                    {
                        cameFrom[link.ToNodeId] = current;
                        cameFromLink[link.ToNodeId] = link;
                        gScore[link.ToNodeId] = tentativeG;

                        var toNode = graphGenerator.GetNode(link.ToNodeId);
                        if (toNode.HasValue)
                        {
                            fScore[link.ToNodeId] = tentativeG + Heuristic(toNode.Value.Position, endNode.Position);
                        }

                        if (!openSet.Contains(link.ToNodeId))
                        {
                            openSet.Add(link.ToNodeId);
                        }
                    }
                }
            }

            // 找不到路径
            return Platform2DPath.NotFound(actualStart, actualEnd);
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

            // 从实际起点走到第一个节点
            if (nodePath.Count > 0)
            {
                var firstNode = graphGenerator.GetNode(nodePath[0]);
                if (firstNode.HasValue)
                {
                    float dist = Vector2.Distance(actualStart, firstNode.Value.Position);
                    if (dist > config.ArriveDistance)
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
                }
            }

            // 处理路径中的每个链接
            Vector3 prevPosition = nodePath.Count > 0 ? graphGenerator.GetNode(nodePath[0])?.Position ?? actualStart : actualStart;

            foreach (var link in linkPath)
            {
                var toNode = graphGenerator.GetNode(link.ToNodeId);
                if (!toNode.HasValue) continue;

                // 计算朝向：根据目标位置与前一个位置的 X 差值
                float deltaX = toNode.Value.Position.x - prevPosition.x;
                int facing = CalculateFacingDirection(deltaX);

                switch (link.LinkType)
                {
                    case PlatformLinkType.Walk:
                        commands.Add(MoveCommand.Walk(toNode.Value.Position, link.Duration, facing));
                        break;

                    case PlatformLinkType.Jump:
                        commands.Add(MoveCommand.Jump(
                            toNode.Value.Position,
                            link.JumpVelocityY,
                            link.JumpVelocityX,
                            link.Duration,
                            link.JumpTrajectory,  // 传递预计算的轨迹点用于可视化
                            facing
                        ));
                        break;

                    case PlatformLinkType.Fall:
                        commands.Add(MoveCommand.Fall(toNode.Value.Position, link.Duration, facing));
                        break;

                    case PlatformLinkType.DropThrough:
                        commands.Add(MoveCommand.DropDown(
                            toNode.Value.Position,
                            toNode.Value.PlatformCollider,
                            link.Duration,
                            facing
                        ));
                        break;
                }

                prevPosition = toNode.Value.Position;
            }

            // 从最后一个节点走到实际终点
            if (nodePath.Count > 0)
            {
                var lastNode = graphGenerator.GetNode(nodePath[nodePath.Count - 1]);
                if (lastNode.HasValue)
                {
                    float dist = Vector2.Distance(lastNode.Value.Position, actualEnd);
                    if (dist > config.ArriveDistance)
                    {
                        // 计算朝向
                        float deltaX = actualEnd.x - lastNode.Value.Position.x;
                        int facing = CalculateFacingDirection(deltaX);

                        commands.Add(MoveCommand.Walk(
                            actualEnd,
                            dist / config.WalkSpeed,
                            facing
                        ));
                    }
                }
            }

            return new Platform2DPath(actualStart, actualEnd, commands);
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
