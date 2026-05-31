using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 任务奖励基类 (v1.2.0+)
    /// 所有任务奖励必须继承此类
    /// </summary>
    [Serializable]
    public abstract class QuestReward
    {
        [Tooltip("奖励描述（用于 UI 显示）")]
        public string Description;

        [Tooltip("是否隐藏（不显示在奖励预览中）")]
        public bool IsHidden;

        /// <summary>
        /// 奖励类型标识
        /// </summary>
        public abstract string RewardType { get; }

        /// <summary>
        /// 发放奖励
        /// </summary>
        /// <returns>是否成功发放</returns>
        public abstract bool Grant();

        /// <summary>
        /// 获取奖励预览文本
        /// </summary>
        public abstract string GetPreviewText();

        /// <summary>
        /// 获取奖励图标（可选）
        /// </summary>
        public virtual Sprite GetIcon() => null;
    }
}