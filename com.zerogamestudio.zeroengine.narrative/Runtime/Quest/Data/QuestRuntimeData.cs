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
        /// Legacy progress list (v1.0 compatibility)
        /// </summary>
        public List<QuestEventProgress> progressList = new List<QuestEventProgress>();

        /// <summary>
        /// Progress dictionary for v1.2.0+ condition system
        /// </summary>
        public Dictionary<string, int> Progress = new Dictionary<string, int>();

        public QuestRuntimeData(string id)
        {
            questId = id;
            state = QuestState.Inactive;
        }

        /// <summary>
        /// Add progress (Legacy system)
        /// </summary>
        public void AddProgress(string targetName, int amount, int maxNeeded)
        {
            QuestEventProgress progress = null;
            for (int i = 0; i < progressList.Count; i++)
            {
                if (progressList[i].targetName == targetName)
                {
                    progress = progressList[i];
                    break;
                }
            }

            if (progress == null)
            {
                progress = new QuestEventProgress { targetName = targetName, currentCount = 0 };
                progressList.Add(progress);
            }

            if (progress.currentCount < maxNeeded)
            {
                progress.currentCount += amount;
            }
        }

        /// <summary>
        /// Get progress (Legacy system)
        /// </summary>
        public int GetProgress(string targetName)
        {
            for (int i = 0; i < progressList.Count; i++)
            {
                if (progressList[i].targetName == targetName)
                    return progressList[i].currentCount;
            }
            return 0;
        }
    }

    [Serializable]
    public class QuestEventProgress
    {
        public string targetName;
        public int currentCount;
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
