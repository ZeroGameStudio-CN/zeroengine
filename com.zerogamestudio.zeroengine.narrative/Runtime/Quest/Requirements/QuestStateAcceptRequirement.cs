using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// Requires another quest to be in a specific state before this quest can be accepted.
    /// </summary>
    [System.Serializable]
    public sealed class QuestStateAcceptRequirement : QuestAcceptRequirement
    {
        [Tooltip("Prerequisite quest ID.")]
        [QuestIdDropdown]
        public string questId;

        [Tooltip("Required state for the prerequisite quest. TheEnd is the normal value for completed quest chains.")]
        public QuestState requiredState = QuestState.TheEnd;

        [Tooltip("Invert the requirement. When enabled, the prerequisite passes when the quest is not in Required State.")]
        public bool invert;

        protected override bool Evaluate(QuestManager questManager, out string reason)
        {
            reason = string.Empty;

            if (questManager == null)
            {
                reason = "QuestManager is not available.";
                return false;
            }

            var trimmedQuestId = questId?.Trim();
            if (string.IsNullOrEmpty(trimmedQuestId))
            {
                reason = "Prerequisite quest id is empty.";
                return false;
            }

            var state = questManager.GetQuestState(trimmedQuestId);
            var matched = state == requiredState;
            var passed = invert ? !matched : matched;
            if (!passed)
            {
                reason = invert
                    ? $"Quest '{trimmedQuestId}' must not be {requiredState}. Current: {state}."
                    : $"Quest '{trimmedQuestId}' must be {requiredState}. Current: {state}.";
            }

            return passed;
        }

        public override string GetPreviewText()
        {
            var id = string.IsNullOrWhiteSpace(questId) ? "<empty>" : questId.Trim();
            return invert
                ? $"{id} != {requiredState}"
                : $"{id} == {requiredState}";
        }
    }
}
