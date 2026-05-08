using System;
using System.Collections.Generic;

namespace ZeroEngine.Quest
{
    /// <summary>
    /// Runtime data for a single active quest.
    /// </summary>
    [Serializable]
    public class QuestRuntimeData
    {
        public string questId;
        public QuestState state;

        /// <summary>
        /// Progress dictionary for condition system.
        /// </summary>
        public Dictionary<string, int> Progress = new Dictionary<string, int>();

        public QuestRuntimeData(string id)
        {
            questId = id;
            state = QuestState.Inactive;
        }

        /// <summary>
        /// Add condition progress.
        /// </summary>
        public void AddProgress(string targetName, int amount, int maxNeeded)
        {
            if (string.IsNullOrEmpty(targetName)) return;

            Progress ??= new Dictionary<string, int>();
            Progress.TryGetValue(targetName, out var current);

            if (current < maxNeeded)
                Progress[targetName] = Math.Min(current + amount, maxNeeded);
        }

        /// <summary>
        /// Get condition progress.
        /// </summary>
        public int GetProgress(string targetName)
        {
            if (string.IsNullOrEmpty(targetName) || Progress == null) return 0;
            return Progress.TryGetValue(targetName, out var current) ? current : 0;
        }
    }

    /// <summary>
    /// Persistent history of completed quests.
    /// </summary>
    [Serializable]
    public class QuestHistoryData
    {
        public string questId;
        public int completionCount;
    }

    /// <summary>
    /// The root container for all quest-related save data.
    /// </summary>
    [Serializable]
    public class QuestSystemSaveData
    {
        public List<QuestRuntimeData> activeQuests = new List<QuestRuntimeData>();
        public List<QuestHistoryData> history = new List<QuestHistoryData>();
    }
}
