using System;

using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就状态
    /// </summary>
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementState")]
    public enum AchievementState
    {
        /// <summary>已锁定（前置未满足）</summary>
        Locked,

        /// <summary>进行中</summary>
        InProgress,

        /// <summary>已完成（未领取奖励）</summary>
        Completed,

        /// <summary>已领取奖励</summary>
        Claimed
    }

    /// <summary>
    /// 成就分类
    /// </summary>
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementCategory")]
    public enum AchievementCategory
    {
        /// <summary>战斗</summary>
        Combat,

        /// <summary>收集</summary>
        Collection,

        /// <summary>探索</summary>
        Exploration,

        /// <summary>社交</summary>
        Social,

        /// <summary>剧情</summary>
        Story,

        /// <summary>制作</summary>
        Crafting,

        /// <summary>隐藏</summary>
        Hidden,

        /// <summary>其他</summary>
        Other
    }

    /// <summary>
    /// 成就事件类型
    /// </summary>
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementEventType")]
    public enum AchievementEventType
    {
        /// <summary>进度更新</summary>
        ProgressUpdated,

        /// <summary>解锁成就</summary>
        Unlocked,

        /// <summary>领取奖励</summary>
        RewardClaimed
    }

    /// <summary>
    /// 成就事件参数
    /// </summary>
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementEventArgs")]
    public struct AchievementEventArgs
    {
        public AchievementEventType EventType;
        public AchievementSO Achievement;
        public float Progress;
        public AchievementState OldState;
        public AchievementState NewState;

        public static AchievementEventArgs ProgressUpdated(AchievementSO achievement, float progress)
        {
            return new AchievementEventArgs
            {
                EventType = AchievementEventType.ProgressUpdated,
                Achievement = achievement,
                Progress = progress
            };
        }

        public static AchievementEventArgs Unlocked(AchievementSO achievement)
        {
            return new AchievementEventArgs
            {
                EventType = AchievementEventType.Unlocked,
                Achievement = achievement,
                OldState = AchievementState.InProgress,
                NewState = AchievementState.Completed
            };
        }

        public static AchievementEventArgs RewardClaimed(AchievementSO achievement)
        {
            return new AchievementEventArgs
            {
                EventType = AchievementEventType.RewardClaimed,
                Achievement = achievement,
                OldState = AchievementState.Completed,
                NewState = AchievementState.Claimed
            };
        }
    }

    /// <summary>
    /// 成就进度数据
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementProgress")]
    public class AchievementProgress
    {
        /// <summary>成就ID</summary>
        public string AchievementId;

        /// <summary>当前状态</summary>
        public AchievementState State;

        /// <summary>条件进度（条件索引 -> 当前值）</summary>
        public System.Collections.Generic.Dictionary<int, int> ConditionProgress =
            new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>解锁时间</summary>
        public long UnlockTimestamp;

        /// <summary>领取时间</summary>
        public long ClaimTimestamp;
    }
}
