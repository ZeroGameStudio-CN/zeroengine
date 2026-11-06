using System;
using UnityEngine;

namespace ZeroEngine.Interaction
{
    /// <summary>
    /// 交互类型枚举 (v1.14.0+)
    /// </summary>
    public enum InteractionType
    {
        Pickup,      // 拾取
        Talk,        // 对话
        Open,        // 打开
        Use,         // 使用
        Examine,     // 检查
        Activate,    // 激活
        Enter,       // 进入
        Craft,       // 制作
        Custom       // 自定义
    }

    /// <summary>
    /// 交互优先级 (v1.14.0+)
    /// </summary>
    public enum InteractionPriority
    {
        Low = 0,
        Normal = 50,
        High = 100,
        Critical = 200
    }

    /// <summary>
    /// 交互上下文 - 只读结构体避免 GC (v1.14.0+)
    /// </summary>
    public readonly struct InteractionContext
    {
        /// <summary>交互者 (通常是玩家)</summary>
        public readonly GameObject Interactor;

        /// <summary>交互发生位置</summary>
        public readonly Vector3 Position;

        /// <summary>交互者与目标的距离</summary>
        public readonly float Distance;

        /// <summary>交互发起时间</summary>
        public readonly float Timestamp;

        public readonly IInteractionRelationshipQuery Relationships;

        public InteractionContext(GameObject interactor, Vector3 position, float distance)
            : this(interactor, position, distance, null) { }

        public InteractionContext(GameObject interactor, Vector3 position, float distance,
            IInteractionRelationshipQuery relationships)
        {
            Relationships = relationships;
            Interactor = interactor;
            Position = position;
            Distance = distance;
            Timestamp = Time.time;
        }
    }

    /// <summary>
    /// 交互结果 (v1.14.0+)
    /// </summary>
    public readonly struct InteractionResult
    {
        /// <summary>是否成功</summary>
        public readonly bool Success;

        /// <summary>失败原因 (成功时为 null)</summary>
        public readonly string FailureReason;

        /// <summary>交互目标</summary>
        public readonly IInteractable Target;

        /// <summary>附加数据</summary>
        public readonly object Data;

        public InteractionResult(bool success, IInteractable target, string failureReason = null, object data = null)
        {
            Success = success;
            Target = target;
            FailureReason = failureReason;
            Data = data;
        }

        public static InteractionResult Succeeded(IInteractable target, object data = null)
            => new InteractionResult(true, target, null, data);

        public static InteractionResult Failed(IInteractable target, string reason)
            => new InteractionResult(false, target, reason, null);
    }

    /// <summary>
    /// 交互事件参数 - 只读结构体避免 GC (v1.14.0+)
    /// </summary>
    public readonly struct InteractionEventArgs
    {
        public readonly IInteractable Interactable;
        public readonly InteractionContext Context;
        public readonly InteractionResult Result;

        public InteractionEventArgs(IInteractable interactable, InteractionContext context, InteractionResult result)
        {
            Interactable = interactable;
            Context = context;
            Result = result;
        }
    }
}
