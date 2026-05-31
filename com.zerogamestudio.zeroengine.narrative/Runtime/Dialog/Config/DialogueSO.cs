using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Dialog
{
    /// <summary>
    /// A single dialogue entry in a DialogueSO.
    /// </summary>
    [Serializable]
    public class DialogueEntry
    {
        [Tooltip("Speaker name (empty for narration)")]
        public string Speaker;

        [TextArea(2, 5)]
        [Tooltip("Dialogue text")]
        public string Text;

        [Tooltip("Localization key (optional)")]
        public string LocalizationKey;

        [Tooltip("Voice clip key (optional)")]
        public string VoiceKey;

        [Tooltip("Portrait/expression key (optional)")]
        public string PortraitKey;

        [Tooltip("Choices at this node (empty for linear flow)")]
        public List<DialogueChoiceEntry> Choices = new();

        [Tooltip("Next entry index (-1 for end, ignored if has choices)")]
        public int NextIndex = -1;
    }

    /// <summary>
    /// A choice within a DialogueEntry.
    /// Named differently from DialogChoice (runtime struct) to avoid confusion.
    /// </summary>
    [Serializable]
    public class DialogueChoiceEntry
    {
        [Tooltip("Choice display text")]
        public string Text;

        [Tooltip("Localization key (optional)")]
        public string LocalizationKey;

        [Tooltip("Target entry index after selection")]
        public int TargetIndex;

        [Tooltip("Condition variable name (optional, checked for truthiness)")]
        public string Condition;
    }

    /// <summary>
    /// ScriptableObject-based dialogue definition for simple conversations.
    /// </summary>
    [CreateAssetMenu(fileName = "NewDialogue", menuName = "ZeroEngine/Dialog/Dialogue")]
    public class DialogueSO : ScriptableObject
    {
        [Tooltip("Start from this entry index")]
        public int StartIndex = 0;

        [Tooltip("All dialogue entries")]
        public List<DialogueEntry> Entries = new();
    }
}
