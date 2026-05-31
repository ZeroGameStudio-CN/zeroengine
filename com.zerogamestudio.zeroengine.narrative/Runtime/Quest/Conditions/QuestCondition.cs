using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 任务条件基类 (v1.2.0+)
    /// 所有任务条件必须继承此类
    /// </summary>
    [Serializable]
    public abstract class QuestCondition
    {
        [Tooltip("条件描述（用于 UI 显示）")]
        public string Description;

        [Tooltip("是否隐藏（不显示在任务追踪中）")]
        public bool IsHidden;

        /// <summary>
        /// 条件类型标识
        /// </summary>
        public abstract string ConditionType { get; }

        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        /// <param name="runtime">任务运行时数据</param>
        /// <returns>是否满足</returns>
        public abstract bool IsSatisfied(QuestRuntimeData runtime);

        /// <summary>
        /// 获取当前进度（用于 UI 显示）
        /// </summary>
        /// <param name="runtime">任务运行时数据</param>
        /// <returns>当前进度值</returns>
        public abstract int GetCurrentProgress(QuestRuntimeData runtime);

        /// <summary>
        /// 获取目标进度（用于 UI 显示）
        /// </summary>
        /// <returns>目标进度值</returns>
        public abstract int GetTargetProgress();

        /// <summary>
        /// 获取进度显示文本
        /// </summary>
        public virtual string GetProgressText(QuestRuntimeData runtime)
        {
            return $"{GetCurrentProgress(runtime)}/{GetTargetProgress()}";
        }

        /// <summary>
        /// 获取条件的唯一键（用于进度追踪）
        /// </summary>
        public abstract string GetProgressKey();

        /// <summary>
        /// 处理事件，更新进度
        /// </summary>
        /// <param name="runtime">任务运行时数据</param>
        /// <param name="eventType">事件类型</param>
        /// <param name="eventData">事件数据</param>
        /// <returns>是否更新了进度</returns>
        public abstract bool ProcessEvent(QuestRuntimeData runtime, string eventType, object eventData);
    }

    /// <summary>
    /// 条件事件数据 (v1.2.0+)
    /// </summary>
    public struct ConditionEventData
    {
        public string TargetId;
        public int Amount;
        public Vector3 Position;
        public object CustomData;

        public ConditionEventData(string targetId, int amount = 1)
        {
            TargetId = targetId;
            Amount = amount;
            Position = Vector3.zero;
            CustomData = null;
        }
    }
}