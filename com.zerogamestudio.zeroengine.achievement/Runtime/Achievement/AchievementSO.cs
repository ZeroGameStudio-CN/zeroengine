using System;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就数据定义 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "New Achievement", menuName = "ZeroEngine/Achievement/Achievement")]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementSO")]
    public class AchievementSO : ScriptableObject
    {
        [Header("基本信息")]
        [Tooltip("成就唯一ID")]
        public string AchievementId;

        [Tooltip("显示名称")]
        public string DisplayName;

        [Tooltip("成就描述")]
        [TextArea(2, 4)]
        public string Description;

        [Tooltip("成就图标")]
        public Sprite Icon;

        [Tooltip("成就分类")]
        public AchievementCategory Category = AchievementCategory.Other;

        [Tooltip("成就点数")]
        public int Points = 10;

        [Tooltip("是否隐藏成就")]
        public bool IsHidden;

        [Header("完成条件")]
        [Tooltip("完成条件（所有条件需满足）")]
        [SerializeReference]
        public List<AchievementCondition> Conditions = new List<AchievementCondition>();

        [Header("奖励")]
        [Tooltip("完成奖励")]
        [SerializeReference]
        public List<AchievementReward> Rewards = new List<AchievementReward>();

        [Header("前置条件")]
        [Tooltip("前置成就")]
        public List<AchievementSO> Prerequisites = new List<AchievementSO>();

        [Tooltip("需要的角色等级")]
        public int RequiredLevel;

        [Header("高级设置")]
        [Tooltip("是否可重复完成")]
        public bool Repeatable;

        [Tooltip("重复冷却时间（秒）")]
        public float RepeatCooldown;

        [Tooltip("排序优先级")]
        public int SortOrder;

        [Tooltip("标签（用于筛选）")]
        public List<string> Tags = new List<string>();

        /// <summary>
        /// 检查所有条件是否完成
        /// </summary>
        public bool CheckAllConditions(AchievementProgress progress)
        {
            if (Conditions == null || Conditions.Count == 0)
                return true;

            for (int i = 0; i < Conditions.Count; i++)
            {
                if (Conditions[i] != null && !Conditions[i].IsCompleted(progress, i))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 获取总体进度 (0-1)
        /// </summary>
        public float GetOverallProgress(AchievementProgress progress)
        {
            if (Conditions == null || Conditions.Count == 0)
                return 1f;

            float total = 0f;
            int validCount = 0;

            for (int i = 0; i < Conditions.Count; i++)
            {
                if (Conditions[i] != null)
                {
                    total += Conditions[i].GetProgress(progress, i);
                    validCount++;
                }
            }

            return validCount > 0 ? total / validCount : 1f;
        }

        /// <summary>
        /// 处理事件
        /// </summary>
        public void ProcessEvent(string eventId, object data, AchievementProgress progress)
        {
            if (Conditions == null) return;

            for (int i = 0; i < Conditions.Count; i++)
            {
                Conditions[i]?.ProcessEvent(eventId, data, progress, i);
            }
        }

        /// <summary>
        /// 发放所有奖励
        /// </summary>
        public void GrantRewards()
        {
            if (Rewards == null) return;

            for (int i = 0; i < Rewards.Count; i++)
            {
                Rewards[i]?.Grant();
            }
        }

        /// <summary>
        /// 获取奖励描述列表
        /// </summary>
        public void GetRewardDescriptions(List<string> results)
        {
            results.Clear();
            if (Rewards == null) return;

            for (int i = 0; i < Rewards.Count; i++)
            {
                if (Rewards[i] != null)
                {
                    results.Add(Rewards[i].Description);
                }
            }
        }

        /// <summary>
        /// 检查前置条件
        /// </summary>
        public bool CheckPrerequisites(Func<AchievementSO, bool> isCompleted, int characterLevel = 0)
        {
            // 检查等级
            if (RequiredLevel > 0 && characterLevel < RequiredLevel)
                return false;

            // 检查前置成就
            if (Prerequisites != null)
            {
                for (int i = 0; i < Prerequisites.Count; i++)
                {
                    if (Prerequisites[i] != null && !isCompleted(Prerequisites[i]))
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 是否有指定标签
        /// </summary>
        public bool HasTag(string tag)
        {
            if (Tags == null || string.IsNullOrEmpty(tag))
                return false;

            for (int i = 0; i < Tags.Count; i++)
            {
                if (Tags[i] == tag)
                    return true;
            }
            return false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(AchievementId))
            {
                AchievementId = name;
            }
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = name;
            }
        }
#endif
    }

    /// <summary>
    /// 成就组（用于分类或成就链）
    /// </summary>
    [CreateAssetMenu(fileName = "New Achievement Group", menuName = "ZeroEngine/Achievement/Achievement Group")]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementGroupSO")]
    public class AchievementGroupSO : ScriptableObject
    {
        [Tooltip("组ID")]
        public string GroupId;

        [Tooltip("组名称")]
        public string DisplayName;

        [Tooltip("组描述")]
        [TextArea(2, 3)]
        public string Description;

        [Tooltip("组图标")]
        public Sprite Icon;

        [Tooltip("组内成就")]
        public List<AchievementSO> Achievements = new List<AchievementSO>();

        [Tooltip("是否为成就链（按顺序解锁）")]
        public bool IsChain;

        [Tooltip("排序优先级")]
        public int SortOrder;

        /// <summary>
        /// 获取组内已完成数量
        /// </summary>
        public int GetCompletedCount(Func<AchievementSO, bool> isCompleted)
        {
            int count = 0;
            for (int i = 0; i < Achievements.Count; i++)
            {
                if (Achievements[i] != null && isCompleted(Achievements[i]))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// 获取组内总点数
        /// </summary>
        public int GetTotalPoints()
        {
            int total = 0;
            for (int i = 0; i < Achievements.Count; i++)
            {
                if (Achievements[i] != null)
                    total += Achievements[i].Points;
            }
            return total;
        }

        /// <summary>
        /// 获取已获得点数
        /// </summary>
        public int GetEarnedPoints(Func<AchievementSO, bool> isCompleted)
        {
            int earned = 0;
            for (int i = 0; i < Achievements.Count; i++)
            {
                if (Achievements[i] != null && isCompleted(Achievements[i]))
                    earned += Achievements[i].Points;
            }
            return earned;
        }
    }
}
