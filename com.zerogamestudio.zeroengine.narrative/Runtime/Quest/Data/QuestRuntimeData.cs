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
        public string instanceId;
        public string rewardManifest;
        public List<string> grantedRewardIds = new List<string>();

        /// <summary>
        /// Progress dictionary for condition system.
        /// </summary>
        [UnityEngine.SerializeField] public Dictionary<string, int> Progress = new Dictionary<string, int>();

        public QuestRuntimeData(string id)
        {
            questId = id;
            state = QuestState.Inactive;
            instanceId = Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Add condition progress.
        /// </summary>
        public void AddProgress(string targetName, int amount, int maxNeeded)
        {
            TryAddProgress(targetName, amount, maxNeeded);
        }

        public bool TryAddProgress(string targetName, int amount, int maxNeeded)
        {
            if (string.IsNullOrEmpty(targetName) || amount <= 0 || maxNeeded <= 0) return false;

            Progress ??= new Dictionary<string, int>();
            Progress.TryGetValue(targetName, out var current);

            if (current >= maxNeeded) return false;
            Progress[targetName] = (int)Math.Min((long)current + amount, maxNeeded);
            return true;
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
        public const int CurrentSchemaVersion = 1;
        public int schemaVersion = CurrentSchemaVersion;
        public List<QuestRuntimeData> activeQuests = new List<QuestRuntimeData>();
        public List<QuestHistoryData> history = new List<QuestHistoryData>();
        // Explicit run boundary; old saves conservatively keep lifetime limits until their first reset.
        public bool runBaselinesInitialized;
        [UnityEngine.SerializeField] public Dictionary<string, int> runCompletionBaselines = new Dictionary<string, int>();
    }
}
