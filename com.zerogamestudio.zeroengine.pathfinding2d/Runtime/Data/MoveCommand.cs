// MoveCommand.cs
// 移动指令数据结构
// 定义寻路系统输出的单个移动指令

using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 移动指令类型
    /// </summary>
    public enum MoveCommandType
    {
        /// <summary>行走到目标点</summary>
        Walk,
        /// <summary>跳跃到目标点</summary>
        Jump,
        /// <summary>自由落体到目标点</summary>
        Fall,
        /// <summary>穿过单向平台下落</summary>
        DropDown
    }

    /// <summary>
    /// 移动指令
    /// 由 Platform2DPathfinder 生成，供 AI 控制器使用
    /// </summary>
    public struct MoveCommand
    {
        /// <summary>指令类型</summary>
        public MoveCommandType CommandType;

        /// <summary>目标位置</summary>
        public Vector3 Target;

        /// <summary>跳跃时的 Y 方向初速度（仅 Jump 类型有效）</summary>
        public float JumpVelocityY;

        /// <summary>跳跃时的 X 方向初速度（仅 Jump 类型有效）</summary>
        public float JumpVelocityX;

        /// <summary>预计到达时间（秒）</summary>
        public float EstimatedDuration;

        /// <summary>关联的平台碰撞体（可为 null）</summary>
        public Collider2D PlatformCollider;

        /// <summary>是否是单向平台</summary>
        public bool IsOneWayPlatform;

        /// <summary>跳跃轨迹点（仅 Jump 类型有效，用于可视化）</summary>
        public Vector2[] JumpTrajectory;

        /// <summary>
        /// 目标朝向：-1=左, 0=保持当前朝向, 1=右
        /// 由寻路系统预计算，避免业务层二次判断导致震荡
        /// </summary>
        public int FacingDirection;

        /// <summary>跳墙指令是否携带显式墙面接触锚点。</summary>
        public bool HasWallContactAnchor;

        /// <summary>墙面接触锚点的世界 X 坐标。</summary>
        public float WallContactX;

        /// <summary>
        /// 创建行走指令
        /// </summary>
        /// <param name="target">目标位置</param>
        /// <param name="duration">预计时间</param>
        /// <param name="facingDirection">目标朝向：-1=左, 0=保持, 1=右</param>
        public static MoveCommand Walk(Vector3 target, float duration = 0f, int facingDirection = 0)
        {
            return new MoveCommand
            {
                CommandType = MoveCommandType.Walk,
                Target = target,
                JumpVelocityY = 0f,
                JumpVelocityX = 0f,
                EstimatedDuration = duration,
                FacingDirection = facingDirection
            };
        }

        /// <summary>
        /// 创建跳跃指令
        /// </summary>
        /// <param name="target">目标位置</param>
        /// <param name="velocityY">Y 方向初速度</param>
        /// <param name="velocityX">X 方向初速度</param>
        /// <param name="duration">预计时间</param>
        /// <param name="trajectory">跳跃轨迹点</param>
        /// <param name="facingDirection">目标朝向：-1=左, 0=保持, 1=右</param>
        public static MoveCommand Jump(Vector3 target, float velocityY, float velocityX = 0f, float duration = 0f, Vector2[] trajectory = null, int facingDirection = 0)
        {
            return Jump(
                target,
                velocityY,
                velocityX,
                duration,
                trajectory,
                facingDirection,
                false,
                0f);
        }

        public static MoveCommand Jump(
            Vector3 target,
            float velocityY,
            float velocityX,
            float duration,
            Vector2[] trajectory,
            int facingDirection,
            bool hasWallContactAnchor,
            float wallContactX)
        {
            return new MoveCommand
            {
                CommandType = MoveCommandType.Jump,
                Target = target,
                JumpVelocityY = velocityY,
                JumpVelocityX = velocityX,
                EstimatedDuration = duration,
                JumpTrajectory = trajectory,
                FacingDirection = facingDirection,
                HasWallContactAnchor = hasWallContactAnchor,
                WallContactX = wallContactX
            };
        }

        /// <summary>
        /// 创建自由落体指令
        /// </summary>
        /// <param name="target">目标位置</param>
        /// <param name="duration">预计时间</param>
        /// <param name="facingDirection">目标朝向：-1=左, 0=保持, 1=右</param>
        public static MoveCommand Fall(Vector3 target, float duration = 0f, int facingDirection = 0)
        {
            return new MoveCommand
            {
                CommandType = MoveCommandType.Fall,
                Target = target,
                JumpVelocityY = 0f,
                JumpVelocityX = 0f,
                EstimatedDuration = duration,
                FacingDirection = facingDirection
            };
        }

        /// <summary>
        /// 创建穿透平台下落指令
        /// </summary>
        /// <param name="target">目标位置</param>
        /// <param name="platform">平台碰撞体</param>
        /// <param name="duration">预计时间</param>
        /// <param name="facingDirection">目标朝向：-1=左, 0=保持, 1=右</param>
        public static MoveCommand DropDown(Vector3 target, Collider2D platform, float duration = 0f, int facingDirection = 0)
        {
            return new MoveCommand
            {
                CommandType = MoveCommandType.DropDown,
                Target = target,
                JumpVelocityY = 0f,
                JumpVelocityX = 0f,
                EstimatedDuration = duration,
                PlatformCollider = platform,
                IsOneWayPlatform = true,
                FacingDirection = facingDirection
            };
        }

        public override string ToString()
        {
            return $"[{CommandType}] Target: {Target:F2}, VelY: {JumpVelocityY:F1}, VelX: {JumpVelocityX:F1}";
        }
    }
}
