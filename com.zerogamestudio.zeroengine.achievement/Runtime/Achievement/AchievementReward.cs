using System;
using UnityEngine;

using UnityEngine.Scripting.APIUpdating;

namespace ZeroEngine.Achievement
{
    /// <summary>
    /// 成就奖励基类
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementReward")]
    public abstract class AchievementReward
    {
        /// <summary>奖励描述</summary>
        public abstract string Description { get; }

        /// <summary>发放奖励</summary>
        public abstract void Grant();

        /// <summary>获取奖励图标（可选）</summary>
        public virtual Sprite GetIcon() => null;
    }

    /// <summary>
    /// 成就点数奖励
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "AchievementPointReward")]
    public class AchievementPointReward : AchievementReward
    {
        [Tooltip("成就点数")]
        public int Points = 10;

        public override string Description => $"成就点数 +{Points}";

        public override void Grant()
        {
            if (Points <= 0) return;

            var manager = AchievementManager.Instance;
            if (manager != null)
            {
                manager.AddAchievementPoints(Points);
            }
        }
    }

    /// <summary>
    /// 自定义奖励
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "CustomAchievementReward")]
    public class CustomAchievementReward : AchievementReward
    {
        [Tooltip("奖励ID")]
        public string RewardId;

        [Tooltip("奖励描述")]
        public string RewardDescription;

        [Tooltip("自定义数据")]
        public string CustomData;

        public override string Description =>
            !string.IsNullOrEmpty(RewardDescription) ? RewardDescription : $"自定义: {RewardId}";

        public override void Grant()
        {
            if (string.IsNullOrEmpty(RewardId)) return;

            if (_customRewardHandlers.TryGetValue(RewardId, out var handler))
            {
                handler(CustomData);
            }
        }

        // 自定义奖励处理器注册
        private static readonly System.Collections.Generic.Dictionary<string, Action<string>> _customRewardHandlers =
            new System.Collections.Generic.Dictionary<string, Action<string>>();

        public static void RegisterCustomHandler(string id, Action<string> handler)
        {
            _customRewardHandlers[id] = handler;
        }

        public static void UnregisterCustomHandler(string id)
        {
            _customRewardHandlers.Remove(id);
        }
    }

    /// <summary>
    /// 组合奖励
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "CompositeReward")]
    public class CompositeReward : AchievementReward
    {
        [Tooltip("子奖励列表")]
        [SerializeReference]
        public AchievementReward[] SubRewards;

        public override string Description
        {
            get
            {
                if (SubRewards == null || SubRewards.Length == 0)
                    return "无奖励";

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < SubRewards.Length; i++)
                {
                    if (SubRewards[i] != null)
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(SubRewards[i].Description);
                    }
                }
                return sb.ToString();
            }
        }

        public override void Grant()
        {
            if (SubRewards == null) return;

            for (int i = 0; i < SubRewards.Length; i++)
            {
                SubRewards[i]?.Grant();
            }
        }
    }
    /// <summary>
    /// 解锁奖励（解锁内容/功能）
    /// </summary>
    [Serializable]
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "UnlockReward")]
    public class UnlockReward : AchievementReward
    {
        [Tooltip("解锁类型")]
        public UnlockType Type = UnlockType.Feature;

        [Tooltip("解锁ID")]
        public string UnlockId;

        [Tooltip("解锁描述")]
        public string UnlockDescription;

        public override string Description =>
            !string.IsNullOrEmpty(UnlockDescription) ? UnlockDescription : $"解锁: {UnlockId}";

        public override void Grant()
        {
            if (string.IsNullOrEmpty(UnlockId)) return;

            // 通知解锁系统
            _unlockCallbacks.TryGetValue(UnlockId, out var callback);
            callback?.Invoke();
        }

        // 解锁回调注册
        private static readonly System.Collections.Generic.Dictionary<string, Action> _unlockCallbacks =
            new System.Collections.Generic.Dictionary<string, Action>();

        public static void RegisterUnlockCallback(string id, Action callback)
        {
            _unlockCallbacks[id] = callback;
        }

        public static void UnregisterUnlockCallback(string id)
        {
            _unlockCallbacks.Remove(id);
        }
    }

    /// <summary>
    /// 解锁类型
    /// </summary>
    [MovedFrom(true, "ZeroEngine.Achievement", "ZeroEngine.Narrative", "UnlockType")]
    public enum UnlockType
    {
        Feature,
        Recipe,
        Character,
        Area,
        Skill,
        Title
    }
}
