// SpatialGrid2D.cs
// 2D 空间网格索引
// 加速节点查询，O(n) -> O(k), k = 相邻格子节点数

using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 2D 空间网格索引
    /// 将节点按空间位置分组，加速范围查询和最近节点查找
    /// </summary>
    public class SpatialGrid2D
    {
        /// <summary>格子尺寸</summary>
        public float CellSize { get; private set; }

        /// <summary>格子到节点索引映射</summary>
        private Dictionary<long, List<int>> _grid;

        /// <summary>所有节点引用</summary>
        private List<PlatformNodeData> _nodes;

        /// <summary>
        /// 创建空间网格
        /// </summary>
        /// <param name="cellSize">格子尺寸，建议 2-4 单位</param>
        public SpatialGrid2D(float cellSize = 3f)
        {
            CellSize = Mathf.Max(1f, cellSize);
            _grid = new Dictionary<long, List<int>>(256);
        }

        /// <summary>
        /// 根据节点列表构建空间索引
        /// </summary>
        /// <param name="nodes">节点列表</param>
        public void Build(List<PlatformNodeData> nodes)
        {
            _nodes = nodes;
            _grid.Clear();

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                long key = GetCellKey(node.Position);

                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<int>(8);
                    _grid[key] = list;
                }
                list.Add(i);
            }
        }

        /// <summary>
        /// 清除索引
        /// </summary>
        public void Clear()
        {
            _grid.Clear();
            _nodes = null;
        }

        /// <summary>
        /// 查找最近的节点（螺旋搜索算法）
        /// </summary>
        /// <param name="position">查询位置</param>
        /// <param name="maxDistance">最大距离</param>
        /// <returns>最近节点，如果找不到返回 null</returns>
        public PlatformNodeData? FindNearest(Vector2 position, float maxDistance = float.MaxValue)
        {
            if (_nodes == null || _nodes.Count == 0)
                return null;

            // 计算需要搜索的格子半径
            int maxRadius = Mathf.CeilToInt(maxDistance / CellSize);
            maxRadius = Mathf.Max(1, maxRadius);

            int cx = Mathf.FloorToInt(position.x / CellSize);
            int cy = Mathf.FloorToInt(position.y / CellSize);

            PlatformNodeData? nearest = null;
            float nearestDistSq = maxDistance * maxDistance;

            // 螺旋搜索：从中心格子开始，逐层向外扩展
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                bool foundInThisRadius = false;

                // 遍历当前半径的所有格子
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        // 只处理边界格子（内层已处理）
                        if (radius > 0 && Mathf.Abs(dx) < radius && Mathf.Abs(dy) < radius)
                            continue;

                        long key = PackKey(cx + dx, cy + dy);
                        if (!_grid.TryGetValue(key, out var nodeIndices))
                            continue;

                        foreach (int nodeIndex in nodeIndices)
                        {
                            var node = _nodes[nodeIndex];
                            float distSq = (position - (Vector2)node.Position).sqrMagnitude;

                            if (distSq < nearestDistSq)
                            {
                                nearestDistSq = distSq;
                                nearest = node;
                                foundInThisRadius = true;
                            }
                        }
                    }
                }

                // 提前退出优化：如果在当前半径找到节点，且下一半径的最小距离大于当前最近距离，则停止
                if (foundInThisRadius)
                {
                    float nextRadiusMinDist = (radius + 1) * CellSize;
                    if (nextRadiusMinDist * nextRadiusMinDist > nearestDistSq)
                    {
                        break;
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// 查找指定范围内的所有节点
        /// </summary>
        /// <param name="position">查询位置</param>
        /// <param name="range">范围半径</param>
        /// <param name="results">结果列表（调用者提供，避免 GC）</param>
        public void FindNodesInRange(Vector2 position, float range, List<PlatformNodeData> results)
        {
            results.Clear();

            if (_nodes == null || _nodes.Count == 0)
                return;

            int cx = Mathf.FloorToInt(position.x / CellSize);
            int cy = Mathf.FloorToInt(position.y / CellSize);
            int radius = Mathf.CeilToInt(range / CellSize);

            float rangeSq = range * range;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    long key = PackKey(cx + dx, cy + dy);
                    if (!_grid.TryGetValue(key, out var nodeIndices))
                        continue;

                    foreach (int nodeIndex in nodeIndices)
                    {
                        var node = _nodes[nodeIndex];
                        float distSq = (position - (Vector2)node.Position).sqrMagnitude;

                        if (distSq <= rangeSq)
                        {
                            results.Add(node);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 获取指定位置所在格子的节点索引列表
        /// </summary>
        public List<int> GetNodesInCell(Vector2 position)
        {
            long key = GetCellKey(position);
            if (_grid.TryGetValue(key, out var list))
            {
                return list;
            }
            return null;
        }

        /// <summary>
        /// 计算位置对应的格子键
        /// </summary>
        private long GetCellKey(Vector3 position)
        {
            int x = Mathf.FloorToInt(position.x / CellSize);
            int y = Mathf.FloorToInt(position.y / CellSize);
            return PackKey(x, y);
        }

        /// <summary>
        /// 打包格子坐标为唯一键
        /// </summary>
        private long PackKey(int x, int y)
        {
            // 使用 long 存储两个 int，避免哈希冲突
            return ((long)x << 32) | ((long)y & 0xFFFFFFFFL);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 获取调试信息
        /// </summary>
        public string GetDebugInfo()
        {
            if (_nodes == null)
                return "SpatialGrid2D: Not initialized";

            int cellCount = _grid.Count;
            int maxNodesInCell = 0;
            int totalNodes = 0;

            foreach (var kvp in _grid)
            {
                int count = kvp.Value.Count;
                totalNodes += count;
                if (count > maxNodesInCell)
                    maxNodesInCell = count;
            }

            float avgNodesPerCell = cellCount > 0 ? (float)totalNodes / cellCount : 0f;

            return $"SpatialGrid2D: {cellCount} cells, {totalNodes} nodes, " +
                   $"max {maxNodesInCell}/cell, avg {avgNodesPerCell:F1}/cell";
        }
#endif
    }
}
