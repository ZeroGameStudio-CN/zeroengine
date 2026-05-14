using UnityEngine;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// A central prerequisite that must pass before a quest can be accepted.
    /// Empty requirement lists are treated as pass.
    /// </summary>
    [System.Serializable]
    public abstract class QuestAcceptRequirement
    {
        [Tooltip("If enabled, this requirement is ignored without deleting its serialized data.")]
        public bool disabled;

        public bool IsSatisfied(QuestManager questManager, out string reason)
        {
            reason = string.Empty;
            if (disabled) return true;
            return Evaluate(questManager, out reason);
        }

        public virtual string GetPreviewText()
        {
            return GetType().Name;
        }

        protected abstract bool Evaluate(QuestManager questManager, out string reason);
    }
}
