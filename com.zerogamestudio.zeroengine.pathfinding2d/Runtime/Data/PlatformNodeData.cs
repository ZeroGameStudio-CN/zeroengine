// PlatformNodeData.cs
// 平台节点数据结构
// 定义平台图中单个节点的信息

using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 平台节点类型
    /// </summary>
    public enum PlatformNodeType
    {
        /// <summary>平台表面普通点</summary>
        Surface,
        /// <summary>平台左边缘</summary>
        LeftEdge,
        /// <summary>平台右边缘</summary>
        RightEdge,
        /// <summary>单向平台节点</summary>
        OneWay
    }

    /// <summary>
    /// 平台节点数据
    /// 用于存储生成的平台节点信息
    /// </summary>
    [System.Serializable]
    public struct PlatformNodeData
    {
        /// <summary>节点唯一标识</summary>
        public int NodeId;

        /// <summary>节点世界坐标</summary>
        public Vector3 Position;

        /// <summary>节点类型</summary>
        public PlatformNodeType NodeType;

        /// <summary>所属平台碰撞体</summary>
        public Collider2D PlatformCollider;

        /// <summary>是否是单向平台</summary>
        public bool IsOneWay;

        /// <summary>平台表面 Y 坐标</summary>
        public float SurfaceY;

        /// <summary>所属可站立平台段 ID。同一连续表面上的节点共享同一个 ID。</summary>
        public int SurfaceGroupId;

        /// <summary>是否是用于跨高度转换的锚点，而不是平台真实边缘。</summary>
        public bool IsTransitionAnchor;

        /// <summary>节点是否可用</summary>
        public bool IsValid => PlatformCollider != null;

        /// <summary>
        /// 创建表面节点
        /// </summary>
        public static PlatformNodeData CreateSurface(
            int id,
            Vector3 position,
            Collider2D collider,
            bool isOneWay = false,
            int surfaceGroupId = -1)
        {
            return new PlatformNodeData
            {
                NodeId = id,
                Position = position,
                NodeType = PlatformNodeType.Surface,
                PlatformCollider = collider,
                IsOneWay = isOneWay,
                SurfaceY = position.y,
                SurfaceGroupId = surfaceGroupId
            };
        }

        /// <summary>
        /// 创建边缘节点
        /// </summary>
        public static PlatformNodeData CreateEdge(
            int id,
            Vector3 position,
            Collider2D collider,
            bool isLeftEdge,
            bool isOneWay = false,
            int surfaceGroupId = -1,
            bool isTransitionAnchor = false)
        {
            return new PlatformNodeData
            {
                NodeId = id,
                Position = position,
                NodeType = isLeftEdge ? PlatformNodeType.LeftEdge : PlatformNodeType.RightEdge,
                PlatformCollider = collider,
                IsOneWay = isOneWay,
                SurfaceY = position.y,
                SurfaceGroupId = surfaceGroupId,
                IsTransitionAnchor = isTransitionAnchor
            };
        }

        public override string ToString()
        {
            return $"Node[{NodeId}] {NodeType} at {Position:F2} (OneWay: {IsOneWay}, Group: {SurfaceGroupId}, Transition: {IsTransitionAnchor})";
        }
    }

    /// <summary>
    /// 平台链接类型
    /// </summary>
    public enum PlatformLinkType
    {
        /// <summary>同平台行走</summary>
        Walk,
        /// <summary>跳跃到目标平台</summary>
        Jump,
        /// <summary>自由落体</summary>
        Fall,
        /// <summary>穿过单向平台下落</summary>
        DropThrough
    }

    /// <summary>
    /// 平台链接数据
    /// 定义两个节点之间的连接关系
    /// </summary>
    [System.Serializable]
    public struct PlatformLinkData
    {
        /// <summary>起始节点 ID</summary>
        public int FromNodeId;

        /// <summary>目标节点 ID</summary>
        public int ToNodeId;

        /// <summary>链接类型</summary>
        public PlatformLinkType LinkType;

        /// <summary>移动代价</summary>
        public float Cost;

        /// <summary>跳跃所需的 Y 方向初速度</summary>
        public float JumpVelocityY;

        /// <summary>跳跃所需的 X 方向初速度</summary>
        public float JumpVelocityX;

        /// <summary>预计移动时间</summary>
        public float Duration;

        /// <summary>是否是单向链接</summary>
        public bool IsOneWay;

        /// <summary>跳跃轨迹点（仅 Jump 类型有效，用于可视化和碰撞检测）</summary>
        public Vector2[] JumpTrajectory;

        /// <summary>
        /// 创建行走链接
        /// </summary>
        public static PlatformLinkData CreateWalk(int from, int to, float distance)
        {
            return new PlatformLinkData
            {
                FromNodeId = from,
                ToNodeId = to,
                LinkType = PlatformLinkType.Walk,
                Cost = distance,
                Duration = distance / 5f, // 假设行走速度 5m/s
                IsOneWay = false
            };
        }

        /// <summary>
        /// 创建跳跃链接
        /// </summary>
        public static PlatformLinkData CreateJump(int from, int to, float velocityY, float velocityX, float duration, Vector2[] trajectory = null)
        {
            return new PlatformLinkData
            {
                FromNodeId = from,
                ToNodeId = to,
                LinkType = PlatformLinkType.Jump,
                Cost = duration * 2f, // 跳跃代价较高
                JumpVelocityY = velocityY,
                JumpVelocityX = velocityX,
                Duration = duration,
                IsOneWay = true, // 跳跃链接通常是单向的
                JumpTrajectory = trajectory
            };
        }

        /// <summary>
        /// 创建下落链接
        /// </summary>
        public static PlatformLinkData CreateFall(int from, int to, float duration)
        {
            return new PlatformLinkData
            {
                FromNodeId = from,
                ToNodeId = to,
                LinkType = PlatformLinkType.Fall,
                Cost = duration * 1.5f,
                Duration = duration,
                IsOneWay = true
            };
        }

        /// <summary>
        /// 创建穿透下落链接
        /// </summary>
        public static PlatformLinkData CreateDropThrough(int from, int to, float duration)
        {
            return new PlatformLinkData
            {
                FromNodeId = from,
                ToNodeId = to,
                LinkType = PlatformLinkType.DropThrough,
                Cost = duration * 1.5f,
                Duration = duration,
                IsOneWay = true
            };
        }

        public override string ToString()
        {
            return $"Link[{FromNodeId}->{ToNodeId}] {LinkType} Cost: {Cost:F2}";
        }
    }
}
