using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// Component attached to NPCs to provide quests or dialogue.
    /// Equivalent to 'QuestPromulgator' in 2DRpg.
    /// </summary>
    public class QuestProvider : MonoBehaviour
    {
        public string providerId; // NPC ID
        public DialogueConfigSO dialogueConfig;
        
        [Header("Quests Offered")]
        public List<QuestConfigSO> quests;

        public bool HasQuestForPlayer()
        {
            // Check if player can accept any quest
            foreach (var q in quests)
            {
                if (CanAccept(q)) return true;
                if (CanSubmit(q)) return true;
            }
            return false;
        }

        private bool CanAccept(QuestConfigSO quest)
        {
            var state = QuestManager.Instance.GetQuestState(quest.questId);
            if (state == QuestState.Inactive)
            {
                // Check repetition logic handled in Manager, but we can pre-check here if needed
                if(quest.repetitionLimit > 0 && QuestManager.Instance.GetQuestCompletionCount(quest.questId) >= quest.repetitionLimit)
                    return false;
                    
                return true;
            }
            return false;
        }

        private bool CanSubmit(QuestConfigSO quest)
        {
             var state = QuestManager.Instance.GetQuestState(quest.questId);
             return state == QuestState.Successful;
        }

        public void Interact()
        {
            // Logic to open Dialogue UI
            // EventManager.Trigger("ShowDialogue", dialogueConfig, this);
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Interacted with {providerId}");
        }
    }
}
