using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Quest
{
    [CreateAssetMenu(fileName = "NewDialogue", menuName = "ZeroEngine/Narrative/Quests/Dialogue")]
    public class DialogueConfigSO : ScriptableObject
    {
        public string npcName;
        [TextArea(3, 10)] public string defaultText;
        
        public List<DialogueNode> nodes;
    }

    [System.Serializable]
    public class DialogueNode
    {
        public string nodeId;
        [TextArea(3, 5)] public string text;
        public List<DialogueChoice> choices;
    }

    [System.Serializable]
    public class DialogueChoice
    {
        public string text;
        public string nextNodeId; // -1 or empty for end
        
        // Actions
        public bool acceptQuest;
        public string questId;
        
        public bool triggerEvent;
        public string eventName;
    }
}
