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

        [Header("Conditions")]
        [Tooltip("条件系统 - 支持多种条件类型")]
        [SerializeReference]
        public List<QuestCondition> Conditions = new List<QuestCondition>();

        [Header("Rewards")]
        [Tooltip("奖励系统 - 支持多种奖励类型")]
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
        /// 获取所有奖励预览文本。
        /// </summary>
        public List<string> GetRewardPreviews()
        {
            var previews = new List<string>();

            if (Rewards == null) return previews;

            foreach (var reward in Rewards)
            {
                if (reward != null && !reward.IsHidden)
                    previews.Add(reward.GetPreviewText());
            }

            return previews;
        }
    }
}
