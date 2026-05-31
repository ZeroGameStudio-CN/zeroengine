using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Quest
{
    [CreateAssetMenu(fileName = "NewQuestConfig", menuName = "ZeroEngine/Quest/Quest Config")]
    public class QuestConfigSO : ScriptableObject
    {
        [Header("Basic Info")]
        public string questId;
        public string questName;
        [TextArea(3, 5)] public string description;
        public QuestType questType;

        [Header("Completion Logic (Legacy)")]
        [Tooltip("旧版条件配置（向后兼容）")]
        public List<QuestEventConfig> failureConditions;
        public List<QuestEventConfig> completionConditions;

        [Header("Conditions (v1.2.0+)")]
        [Tooltip("新版条件系统 - 支持多种条件类型")]
        [SerializeReference]
        public List<QuestCondition> Conditions = new List<QuestCondition>();

        [Header("Rewards (Legacy)")]
        public int expReward;
        public int goldReward;
        public List<string> itemRewards;

        [Header("Rewards (v1.2.0+)")]
        [Tooltip("新版奖励系统 - 支持多种奖励类型")]
        [SerializeReference]
        public List<QuestReward> Rewards = new List<QuestReward>();

        [Header("Settings")]
        public bool autoSubmit;
        public int repetitionLimit;
        public QuestLifecycle lifecycle = QuestLifecycle.Persistent;

        [Header("NPC Interaction")]
        public string providerNpcId;
        public string submitNpcId;
        [TextArea(3, 5)] public string completionDialogue;

        /// <summary>
        /// 是否使用新版条件系统 (v1.2.0+)
        /// </summary>
        public bool UsesNewConditionSystem => Conditions != null && Conditions.Count > 0;

        /// <summary>
        /// 是否使用新版奖励系统 (v1.2.0+)
        /// </summary>
        public bool UsesNewRewardSystem => Rewards != null && Rewards.Count > 0;

        /// <summary>
        /// 获取所有奖励预览文本 (v1.2.0+)
        /// </summary>
        public List<string> GetRewardPreviews()
        {
            var previews = new List<string>();

            // Legacy rewards
            if (expReward > 0)
                previews.Add($"经验值 +{expReward}");
            if (goldReward > 0)
                previews.Add($"金币 +{goldReward}");
            foreach (var itemId in itemRewards)
            {
                if (!string.IsNullOrEmpty(itemId))
                    previews.Add(itemId);
            }

            // New rewards
            if (Rewards != null)
            {
                foreach (var reward in Rewards)
                {
                    if (reward != null && !reward.IsHidden)
                        previews.Add(reward.GetPreviewText());
                }
            }

            return previews;
        }

#if UNITY_EDITOR
        [ContextMenu("Migrate Legacy Conditions to New System")]
        private void MigrateLegacyConditions()
        {
            if (completionConditions == null || completionConditions.Count == 0) return;
            if (Conditions == null) Conditions = new List<QuestCondition>();

            foreach (var legacy in completionConditions)
            {
                QuestCondition newCondition = questType switch
                {
                    QuestType.KillMonster => new KillCondition
                    {
                        TargetId = legacy.targetName,
                        RequiredCount = legacy.targetCount,
                        Description = $"击杀 {legacy.targetName}"
                    },
                    QuestType.Collect => new CollectCondition
                    {
                        ItemId = legacy.targetName,
                        RequiredCount = legacy.targetCount,
                        Description = $"收集 {legacy.targetName}"
                    },
                    QuestType.Dialogue => new InteractCondition
                    {
                        TargetId = legacy.targetName,
                        RequiredCount = legacy.targetCount,
                        InteractionType = InteractionType.Talk,
                        Description = $"与 {legacy.targetName} 对话"
                    },
                    _ => new InteractCondition
                    {
                        TargetId = legacy.targetName,
                        RequiredCount = legacy.targetCount,
                        Description = legacy.targetName
                    }
                };

                Conditions.Add(newCondition);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Quest] Migrated {completionConditions.Count} conditions to new system");
        }

        [ContextMenu("Migrate Legacy Rewards to New System")]
        private void MigrateLegacyRewards()
        {
            if (Rewards == null) Rewards = new List<QuestReward>();

            if (expReward > 0)
            {
                Rewards.Add(new ExpReward { Amount = expReward });
            }

            if (goldReward > 0)
            {
                Rewards.Add(new CurrencyReward
                {
                    CurrencyType = CurrencyType.Gold,
                    Amount = goldReward
                });
            }

            foreach (var itemId in itemRewards)
            {
                if (!string.IsNullOrEmpty(itemId))
                {
                    Rewards.Add(new ItemReward { ItemId = itemId, Quantity = 1 });
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"[Quest] Migrated legacy rewards to new system");
        }
#endif
    }

    [System.Serializable]
    public class QuestEventConfig
    {
        public string targetName;
        public int targetCount;
    }
}

