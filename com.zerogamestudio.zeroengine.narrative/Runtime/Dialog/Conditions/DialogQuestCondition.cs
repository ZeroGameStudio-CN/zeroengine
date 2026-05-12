using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Quest;

namespace ZeroEngine.Dialog
{
    public enum DialogQuestConditionMode
    {
        ExactState,
        Active,
        CanSubmit
    }

    /// <summary>
    /// Structured quest condition for dialogue branches and choices.
    /// </summary>
    [Serializable]
    public class DialogQuestCondition
    {
        [Tooltip("QuestConfigSO.questId to check. Empty means this condition is ignored.")]
        [QuestIdDropdown]
        public string questId;

        [Tooltip("How to check the quest state.")]
        public DialogQuestConditionMode mode = DialogQuestConditionMode.ExactState;

        [Tooltip("Required state when Mode is ExactState.")]
        public QuestState requiredState = QuestState.Active;

        [Tooltip("Invert the final condition result.")]
        public bool invert;

        public bool Evaluate()
        {
            var result = EvaluateCore();
            return invert ? !result : result;
        }

        private bool EvaluateCore()
        {
            if (string.IsNullOrWhiteSpace(questId))
                return true;

            var questManager = QuestManager.Instance;
            if (questManager == null)
                return false;

            var trimmedQuestId = questId.Trim();
            return mode switch
            {
                DialogQuestConditionMode.Active => questManager.HasActiveQuest(trimmedQuestId),
                DialogQuestConditionMode.CanSubmit => questManager.GetQuestState(trimmedQuestId) == QuestState.Successful,
                _ => questManager.GetQuestState(trimmedQuestId) == requiredState
            };
        }
    }

    /// <summary>
    /// Group of structured quest conditions. Empty groups pass.
    /// </summary>
    [Serializable]
    public class DialogQuestConditionGroup
    {
        [Tooltip("How to combine quest conditions in this group.")]
        public LogicalOperator CombineWith = LogicalOperator.And;

        [Tooltip("Quest conditions. Empty means always available.")]
        public List<DialogQuestCondition> Conditions = new();

        public bool Evaluate()
        {
            if (Conditions == null || Conditions.Count == 0)
                return true;

            if (CombineWith == LogicalOperator.Or)
            {
                for (int i = 0; i < Conditions.Count; i++)
                {
                    if (Conditions[i] != null && Conditions[i].Evaluate())
                        return true;
                }

                return false;
            }

            for (int i = 0; i < Conditions.Count; i++)
            {
                if (Conditions[i] != null && !Conditions[i].Evaluate())
                    return false;
            }

            return true;
        }
    }
}
