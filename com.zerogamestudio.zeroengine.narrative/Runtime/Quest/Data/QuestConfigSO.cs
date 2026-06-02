using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Quest
{
    [CreateAssetMenu(fileName = "NewQuestConfig", menuName = "ZeroEngine/Quest/Quest Config")]
    public class QuestConfigSO : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("Unique quest ID. Keep stable after release because runtime state and save data reference this value.")]
        public string questId;

        [Tooltip("Fallback display name. Projects may override this through their localization layer.")]
        public string questName;

        [Tooltip("Fallback quest description for UI or designer notes.")]
        [TextArea(3, 5)] public string description;

        [Tooltip("Legacy quest type retained for projects that still author simple objective templates.")]
        public QuestType questType;

        [Header("Completion Logic (Legacy)")]
        [Tooltip("Legacy failure conditions retained for existing content migration.")]
        public List<QuestEventConfig> failureConditions;

        [Tooltip("Legacy completion conditions retained for existing content migration.")]
        public List<QuestEventConfig> completionConditions;

        [Header("Conditions")]
        [Tooltip("Completion conditions. All visible and hidden conditions must be satisfied before the quest becomes Successful.")]
        [SerializeReference]
        public List<QuestCondition> Conditions = new List<QuestCondition>();

        [Header("Accept Requirements")]
        [Tooltip("Optional prerequisites that must pass before this quest can be accepted. Empty means always accept-ready.")]
        [SerializeReference]
        public List<QuestAcceptRequirement> AcceptRequirements = new List<QuestAcceptRequirement>();

        [Header("Rewards")]
        [Tooltip("Legacy experience reward retained for existing content migration.")]
        public int expReward;

        [Tooltip("Legacy gold reward retained for existing content migration.")]
        public int goldReward;

        [Tooltip("Legacy item rewards retained for existing content migration.")]
        public List<string> itemRewards;

        [Tooltip("Rewards granted when the quest is submitted or auto-submitted.")]
        [SerializeReference]
        public List<QuestReward> Rewards = new List<QuestReward>();

        [Header("Settings")]
        [Tooltip("If enabled, the quest submits automatically as soon as all Conditions are satisfied.")]
        public bool autoSubmit;

        [Min(0)]
        [Tooltip("How many times this quest may be completed. 0 means unlimited.")]
        public int repetitionLimit;

        [Tooltip("Persistent quests stay in save data. PerRun quests can be cleared by run/session flow.")]
        public QuestLifecycle lifecycle = QuestLifecycle.Persistent;

        [Header("NPC Interaction")]
        [Tooltip("Optional provider NPC ID for projects that use NPC-based quest acceptance.")]
        [QuestNpcIdDropdown]
        public string providerNpcId;

        [Tooltip("Optional submit NPC ID for projects that require hand-in at a specific NPC.")]
        [QuestNpcIdDropdown]
        public string submitNpcId;

        [Tooltip("Optional fallback completion dialogue text.")]
        [TextArea(3, 5)] public string completionDialogue;

        /// <summary>
        /// 是否使用新版条件系统。
        /// </summary>
        public bool UsesNewConditionSystem => Conditions != null && Conditions.Count > 0;

        /// <summary>
        /// 是否使用新版奖励系统。
        /// </summary>
        public bool UsesNewRewardSystem => Rewards != null && Rewards.Count > 0;

        /// <summary>
        /// 获取所有奖励预览文本。
        /// </summary>
        public List<string> GetRewardPreviews()
        {
            var previews = new List<string>();

            if (expReward > 0)
            {
                previews.Add($"经验值 +{expReward}");
            }

            if (goldReward > 0)
            {
                previews.Add($"金币 +{goldReward}");
            }

            if (itemRewards != null)
            {
                foreach (var itemId in itemRewards)
                {
                    if (!string.IsNullOrEmpty(itemId))
                    {
                        previews.Add(itemId);
                    }
                }
            }

            if (Rewards == null) return previews;

            foreach (var reward in Rewards)
            {
                if (reward != null && !reward.IsHidden)
                    previews.Add(reward.GetPreviewText());
            }

            return previews;
        }
    }

    [System.Serializable]
    public class QuestEventConfig
    {
        public string targetName;
        public int targetCount;
    }
}
