using System;
using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// 任务奖励基类。
    /// 所有任务奖励必须继承此类
    /// </summary>
    [Serializable]
    public abstract class QuestReward
    {
        [Tooltip("Stable receipt identity within this quest. Assign before release; empty uses the legacy list position.")]
        public string RewardId;

        [Header("Display")]
        [Tooltip("Fallback reward description for UI preview. Projects may override this through localization.")]
        public string Description;

        [Tooltip("Hide this reward from preview UI while still granting it.")]
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
        /// Stable authored grant payload. Rewards with Unity object references must override this
        /// using catalog identities rather than session-specific object instance IDs.
        /// </summary>
        public virtual string GetGrantFingerprint() => JsonUtility.ToJson(this);

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
