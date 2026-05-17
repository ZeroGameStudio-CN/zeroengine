// Platform2DPath.cs
// 2D 平台路径数据结构
// 封装从起点到终点的完整路径信息

using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Pathfinding2D
{
    /// <summary>
    /// 路径状态
    /// </summary>
    public enum PathStatus
    {
        /// <summary>路径有效</summary>
        Valid,
        /// <summary>路径计算中</summary>
        Pending,
        /// <summary>找不到路径</summary>
        NotFound,
        /// <summary>路径已过期</summary>
        Stale,
        /// <summary>路径出错</summary>
        Error
    }

    /// <summary>
    /// 2D 平台路径
    /// 包含从起点到终点的一系列移动指令
    /// </summary>
    public class Platform2DPath
    {
        /// <summary>路径状态</summary>
        public PathStatus Status { get; private set; } = PathStatus.Pending;

        /// <summary>路径完成类型，用于区分完整路径、已到达、部分路径和失败。</summary>
        public PlatformPathCompletionKind CompletionKind { get; private set; } = PlatformPathCompletionKind.Failed;

        /// <summary>移动指令列表</summary>
        public List<MoveCommand> Commands { get; private set; } = new List<MoveCommand>();

        /// <summary>当前指令索引</summary>
        public int CurrentIndex { get; private set; }

        /// <summary>起点位置</summary>
        public Vector3 StartPosition { get; private set; }

        /// <summary>终点位置</summary>
        public Vector3 EndPosition { get; private set; }

        /// <summary>路径创建时间</summary>
        public float CreateTime { get; private set; }

        /// <summary>预计总耗时</summary>
        public float TotalDuration { get; private set; }

        public PlatformRouteCost RouteCost { get; private set; } = PlatformRouteCost.Unreachable;
        public bool CommandsValidated { get; private set; }

        /// <summary>是否还有剩余指令</summary>
        public bool HasNextCommand => CurrentIndex < Commands.Count;

        /// <summary>是否已完成</summary>
        public bool IsComplete => CurrentIndex >= Commands.Count;

        /// <summary>剩余指令数</summary>
        public int RemainingCount => Mathf.Max(0, Commands.Count - CurrentIndex);

        /// <summary>
        /// 创建空路径
        /// </summary>
        public Platform2DPath()
        {
            CreateTime = Time.time;
        }

        /// <summary>
        /// 创建有效路径
        /// </summary>
        public Platform2DPath(
            Vector3 start,
            Vector3 end,
            List<MoveCommand> commands,
            PlatformPathCompletionKind completionKind = PlatformPathCompletionKind.FullPath)
        {
            StartPosition = start;
            EndPosition = end;
            Commands = commands ?? new List<MoveCommand>();
            Status = Commands.Count > 0 ? PathStatus.Valid : PathStatus.NotFound;
            CompletionKind = Status == PathStatus.Valid ? completionKind : PlatformPathCompletionKind.Failed;
            CreateTime = Time.time;

            // 计算总耗时
            TotalDuration = 0f;
            foreach (var cmd in Commands)
            {
                TotalDuration += cmd.EstimatedDuration;
            }
        }

        /// <summary>
        /// 获取当前指令
        /// </summary>
        public MoveCommand? GetCurrentCommand()
        {
            if (CurrentIndex < Commands.Count)
            {
                return Commands[CurrentIndex];
            }
            return null;
        }

        /// <summary>
        /// 前进到下一个指令
        /// </summary>
        public bool AdvanceToNext()
        {
            if (CurrentIndex < Commands.Count)
            {
                CurrentIndex++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 重置到起点
        /// </summary>
        public void Reset()
        {
            CurrentIndex = 0;
        }

        /// <summary>
        /// 标记路径状态
        /// </summary>
        public void SetStatus(PathStatus status)
        {
            Status = status;
        }

        public void SetRouteDiagnostics(PlatformRouteCost routeCost, bool commandsValidated)
        {
            RouteCost = routeCost;
            CommandsValidated = commandsValidated;
        }

        /// <summary>
        /// 检查路径是否过期
        /// </summary>
        /// <param name="maxAge">最大存活时间（秒）</param>
        public bool IsExpired(float maxAge)
        {
            return Time.time - CreateTime > maxAge;
        }

        /// <summary>
        /// 创建找不到路径的结果
        /// </summary>
        public static Platform2DPath NotFound(Vector3 start, Vector3 end)
        {
            return new Platform2DPath
            {
                StartPosition = start,
                EndPosition = end,
                Status = PathStatus.NotFound,
                CompletionKind = PlatformPathCompletionKind.Failed
            };
        }

        /// <summary>
        /// 创建已到达目标的有效空路径。
        /// </summary>
        public static Platform2DPath Arrived(Vector3 start, Vector3 end)
        {
            return new Platform2DPath
            {
                StartPosition = start,
                EndPosition = end,
                Commands = new List<MoveCommand>(),
                Status = PathStatus.Valid,
                CompletionKind = PlatformPathCompletionKind.Arrived,
                CreateTime = Time.time
            };
        }

        /// <summary>
        /// 创建部分路径。部分路径仅表示最近可达点，不代表完整到达目标。
        /// </summary>
        public static Platform2DPath Partial(Vector3 start, Vector3 end, List<MoveCommand> commands)
        {
            var path = new Platform2DPath(start, end, commands, PlatformPathCompletionKind.Partial);
            path.Status = commands != null && commands.Count > 0 ? PathStatus.Valid : PathStatus.NotFound;
            path.CompletionKind = path.Status == PathStatus.Valid
                ? PlatformPathCompletionKind.Partial
                : PlatformPathCompletionKind.Failed;
            return path;
        }

        /// <summary>
        /// 创建待处理路径
        /// </summary>
        public static Platform2DPath Pending(Vector3 start, Vector3 end)
        {
            return new Platform2DPath
            {
                StartPosition = start,
                EndPosition = end,
                Status = PathStatus.Pending,
                CompletionKind = PlatformPathCompletionKind.Failed
            };
        }

        public override string ToString()
        {
            return $"[Path] Status: {Status}, Commands: {Commands.Count}, Current: {CurrentIndex}/{Commands.Count}";
        }
    }
}
