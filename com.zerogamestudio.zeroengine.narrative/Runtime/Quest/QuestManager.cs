using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    public class QuestManager : Singleton<QuestManager>, ISaveable
    {
        // Runtime Data
        private QuestSystemSaveData _saveData = new QuestSystemSaveData();

        // Config Cache
        private Dictionary<string, QuestConfigSO> _questConfigs = new Dictionary<string, QuestConfigSO>();

        #region Events (v1.2.0+)

        /// <summary>
        /// 条件进度更新时触发
        /// </summary>
        public event Action<string, QuestCondition> OnConditionProgress;

        /// <summary>
        /// 单个条件完成时触发
        /// </summary>
        public event Action<string, QuestCondition> OnConditionCompleted;

        #endregion

        protected override void Awake()
        {
            base.Awake();
            LoadConfig();
        }

        private void Start()
        {
            RegisterEvents();

            // 注册到存档系统
            SaveSlotManager.Instance?.Register(this);
        }

        protected override void OnDestroy()
        {
            UnregisterEvents();
            SaveSlotManager.Instance?.Unregister(this);
            base.OnDestroy();
        }

        #region ISaveable Implementation

        /// <summary>
        /// ISaveable: 存档键名
        /// </summary>
        public string SaveKey => "Quest";

        /// <summary>
        /// ISaveable: 导出存档数据
        /// </summary>
        public object ExportSaveData()
        {
            return _saveData;
        }

        /// <summary>
        /// ISaveable: 导入存档数据
        /// </summary>
        public void ImportSaveData(object data)
        {
            if (data is QuestSystemSaveData questData)
            {
                _saveData = questData;
            }
            else
            {
                _saveData = new QuestSystemSaveData();
            }
        }

        /// <summary>
        /// ISaveable: 重置为初始状态
        /// </summary>
        public void ResetToDefault()
        {
            _saveData = new QuestSystemSaveData();
        }

        #endregion

        private void LoadConfig()
        {
            var configs = Resources.LoadAll<QuestConfigSO>("Quests");
            foreach (var config in configs)
            {
                if (!_questConfigs.ContainsKey(config.questId))
                    _questConfigs.Add(config.questId, config);
            }
        }

        /// <summary>
        /// 注册任务配置 (v1.2.0+)
        /// </summary>
        public void RegisterConfig(QuestConfigSO config)
        {
            if (config != null && !string.IsNullOrEmpty(config.questId))
            {
                _questConfigs[config.questId] = config;
            }
        }

        #region Event Management

        private void RegisterEvents()
        {
            // Legacy events
            EventManager.Subscribe<string, int>(GameEvents.ItemObtained, OnItemObtained);
            EventManager.Subscribe<string, int>(GameEvents.CharacterDied, OnEntityKilled);
            EventManager.Subscribe<string>(GameEvents.QuestProgressChanged, OnManualProgress);

            // v1.2.0+ condition events — each handler forwards eventType to ProcessConditionEvent
            EventManager.Subscribe<ConditionEventData>(QuestEvents.EntityKilled, OnEntityKilledCondition);
            EventManager.Subscribe<ConditionEventData>(QuestEvents.ItemObtained, OnItemObtainedCondition);
            EventManager.Subscribe<ConditionEventData>(QuestEvents.Interacted, OnInteractedCondition);
            EventManager.Subscribe<ConditionEventData>(QuestEvents.LocationReached, OnLocationReachedCondition);
            EventManager.Subscribe<ConditionEventData>(QuestEvents.SurviveCompleted, OnSurviveCompletedCondition);
        }

        private void UnregisterEvents()
        {
            EventManager.Unsubscribe<string, int>(GameEvents.ItemObtained, OnItemObtained);
            EventManager.Unsubscribe<string, int>(GameEvents.CharacterDied, OnEntityKilled);
            EventManager.Unsubscribe<string>(GameEvents.QuestProgressChanged, OnManualProgress);

            EventManager.Unsubscribe<ConditionEventData>(QuestEvents.EntityKilled, OnEntityKilledCondition);
            EventManager.Unsubscribe<ConditionEventData>(QuestEvents.ItemObtained, OnItemObtainedCondition);
            EventManager.Unsubscribe<ConditionEventData>(QuestEvents.Interacted, OnInteractedCondition);
            EventManager.Unsubscribe<ConditionEventData>(QuestEvents.LocationReached, OnLocationReachedCondition);
            EventManager.Unsubscribe<ConditionEventData>(QuestEvents.SurviveCompleted, OnSurviveCompletedCondition);
        }

        // Legacy event handlers
        private void OnItemObtained(string itemId, int count)
        {
            UpdateQuestProgress(QuestType.Collect, itemId, count);
            ProcessConditionEvent(QuestEvents.ItemObtained, new ConditionEventData(itemId, count));
        }

        private void OnEntityKilled(string entityId, int count)
        {
            UpdateQuestProgress(QuestType.KillMonster, entityId, count);
            ProcessConditionEvent(QuestEvents.EntityKilled, new ConditionEventData(entityId, count));
        }

        private void OnManualProgress(string targetName)
        {
            UpdateQuestProgress(QuestType.Custom, targetName, 1);
            UpdateQuestProgress(QuestType.Dialogue, targetName, 1);
            ProcessConditionEvent(QuestEvents.Interacted, new ConditionEventData(targetName, 1));
        }

        // v1.2.0+ per-event condition handlers
        private void OnEntityKilledCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.EntityKilled, data);
        private void OnItemObtainedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.ItemObtained, data);
        private void OnInteractedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.Interacted, data);
        private void OnLocationReachedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.LocationReached, data);
        private void OnSurviveCompletedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.SurviveCompleted, data);

        #endregion

        #region Core Logic

        /// <summary>
        /// 处理条件事件 (v1.2.0+)
        /// </summary>
        public void ProcessConditionEvent(string eventType, ConditionEventData data)
        {
            for (int i = 0; i < _saveData.activeQuests.Count; i++)
            {
                var quest = _saveData.activeQuests[i];
                if (quest.state != QuestState.Active) continue;

                var config = GetConfig(quest.questId);
                if (config == null || !config.UsesNewConditionSystem) continue;

                bool questChanged = false;

                foreach (var condition in config.Conditions)
                {
                    if (condition == null) continue;

                    bool wasCompleted = condition.IsSatisfied(quest);
                    bool updated = condition.ProcessEvent(quest, eventType, data);

                    if (updated)
                    {
                        questChanged = true;
                        OnConditionProgress?.Invoke(quest.questId, condition);

                        if (!wasCompleted && condition.IsSatisfied(quest))
                        {
                            OnConditionCompleted?.Invoke(quest.questId, condition);
                        }
                    }
                }

                if (questChanged)
                {
                    CheckCompletionNew(quest, config);
                }
            }
        }

        /// <summary>
        /// 更新任务进度 (Legacy)
        /// </summary>
        public void UpdateQuestProgress(QuestType type, string targetName, int amount)
        {

            foreach (var quest in _saveData.activeQuests)
            {
                if (quest.state != QuestState.Active) continue;

                var config = GetConfig(quest.questId);
                if (config == null || config.questType != type) continue;
                if (config.UsesNewConditionSystem) continue;

                if (config.completionConditions == null) continue;
                QuestEventConfig condition = null;
                for (int j = 0; j < config.completionConditions.Count; j++)
                {
                    if (config.completionConditions[j].targetName == targetName)
                    {
                        condition = config.completionConditions[j];
                        break;
                    }
                }
                if (condition != null)
                {
                    quest.AddProgress(targetName, amount, condition.targetCount);
                    ZeroLog.Info(ZeroLog.Modules.Quest, $"Updated {quest.questId}: {targetName} += {amount}");

                    CheckCompletionLegacy(quest, config);
                }
            }
        }

        /// <summary>
        /// 检查完成状态 (Legacy)
        /// </summary>
        private void CheckCompletionLegacy(QuestRuntimeData quest, QuestConfigSO config)
        {
            if (config.completionConditions == null) return;

            bool isComplete = true;
            foreach (var condition in config.completionConditions)
            {
                int current = quest.GetProgress(condition.targetName);
                if (current < condition.targetCount)
                {
                    isComplete = false;
                    break;
                }
            }

            if (isComplete)
            {
                CompleteQuest(quest, config);
            }
        }

        /// <summary>
        /// 检查完成状态 (v1.2.0+)
        /// </summary>
        private void CheckCompletionNew(QuestRuntimeData quest, QuestConfigSO config)
        {
            if (config.Conditions == null || config.Conditions.Count == 0) return;

            bool isComplete = true;
            foreach (var condition in config.Conditions)
            {
                if (condition != null && !condition.IsSatisfied(quest))
                {
                    isComplete = false;
                    break;
                }
            }

            if (isComplete)
            {
                CompleteQuest(quest, config);
            }
        }

        private void CompleteQuest(QuestRuntimeData quest, QuestConfigSO config)
        {
            quest.state = QuestState.Successful;
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Quest '{quest.questId}' Conditions Met!");
            EventManager.Trigger(GameEvents.QuestCompleted, quest.questId);

            if (config.autoSubmit)
            {
                SubmitQuest(quest.questId);
            }
        }

        public bool AcceptQuest(string questId)
        {
            if (HasActiveQuest(questId))
            {
                ZeroLog.Warning(ZeroLog.Modules.Quest, $"Already has active quest: {questId}");
                return false;
            }

            var config = GetConfig(questId);
            if (config == null) return false;

            if (config.repetitionLimit > 0)
            {
                int doneCount = GetQuestCompletionCount(questId);
                if (doneCount >= config.repetitionLimit)
                {
                    ZeroLog.Warning(ZeroLog.Modules.Quest, $"Repetition limit reached for: {questId}");
                    return false;
                }
            }

            var newQuest = new QuestRuntimeData(questId)
            {
                state = QuestState.Active
            };
            _saveData.activeQuests.Add(newQuest);

            ZeroLog.Info(ZeroLog.Modules.Quest, $"Accepted: {questId}");
            EventManager.Trigger(GameEvents.QuestAccepted, questId);
            return true;
        }

        public void SubmitQuest(string questId)
        {
            var quest = FindActiveQuest(questId);
            if (quest == null || quest.state != QuestState.Successful)
            {
                ZeroLog.Warning(ZeroLog.Modules.Quest, $"Cannot submit {questId}. Not active or not completed.");
                return;
            }

            var config = GetConfig(questId);
            if (config != null)
            {
                GrantRewards(config);
            }

            quest.state = QuestState.TheEnd;
            _saveData.activeQuests.Remove(quest);

            var history = FindHistory(questId);
            if (history == null)
            {
                history = new QuestHistoryData { questId = questId, completionCount = 0 };
                _saveData.history.Add(history);
            }
            history.completionCount++;

            ZeroLog.Info(ZeroLog.Modules.Quest, $"Submitted: {questId}");
            EventManager.Trigger(QuestEvents.QuestSubmitted, questId);
        }

        /// <summary>
        /// 发放奖励 (v1.2.0+)
        /// </summary>
        private void GrantRewards(QuestConfigSO config)
        {
            // Legacy rewards
            if (config.expReward > 0)
            {
                EventManager.Trigger(GameEvents.ExpGained, config.expReward);
            }

            if (config.goldReward > 0)
            {
                EventManager.Trigger(GameEvents.CurrencyGained, "Gold", config.goldReward);
            }

            // v1.2.0+ rewards
            if (config.Rewards != null)
            {
                foreach (var reward in config.Rewards)
                {
                    if (reward != null)
                    {
                        reward.Grant();
                    }
                }
            }
        }

        /// <summary>
        /// 放弃任务 (v1.2.0+)
        /// </summary>
        public void AbandonQuest(string questId)
        {
            var quest = FindActiveQuest(questId);
            if (quest == null)
            {
                ZeroLog.Warning(ZeroLog.Modules.Quest, $"Quest not found: {questId}");
                return;
            }

            _saveData.activeQuests.Remove(quest);
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Abandoned: {questId}");
            EventManager.Trigger(QuestEvents.QuestAbandoned, questId);
        }

        #endregion

        #region Query Methods (v1.2.0+)

        // Reusable buffer for condition progress queries
        private readonly List<(QuestCondition condition, int current, int target, bool completed)> _conditionProgressBuffer
            = new List<(QuestCondition, int, int, bool)>(8);

        /// <summary>
        /// 获取任务的条件进度列表
        /// </summary>
        public List<(QuestCondition condition, int current, int target, bool completed)> GetConditionProgress(string questId)
        {
            var result = new List<(QuestCondition, int, int, bool)>();
            GetConditionProgressNonAlloc(questId, result);
            return result;
        }

        /// <summary>
        /// 获取任务的条件进度列表 (零分配版本, v1.2.0+)
        /// </summary>
        /// <param name="questId">任务ID</param>
        /// <param name="buffer">用于存储结果的列表，会被清空后填充</param>
        /// <returns>填充的条目数量</returns>
        public int GetConditionProgressNonAlloc(string questId, List<(QuestCondition condition, int current, int target, bool completed)> buffer)
        {
            buffer.Clear();

            var quest = FindActiveQuest(questId);
            if (quest == null) return 0;

            var config = GetConfig(questId);
            if (config == null || !config.UsesNewConditionSystem) return 0;

            foreach (var condition in config.Conditions)
            {
                if (condition == null || condition.IsHidden) continue;

                int current = condition.GetCurrentProgress(quest);
                int target = condition.GetTargetProgress();
                bool completed = condition.IsSatisfied(quest);

                buffer.Add((condition, current, target, completed));
            }

            return buffer.Count;
        }

        /// <summary>
        /// 获取任务的条件进度列表 (使用内部缓冲区, v1.2.0+)
        /// 注意：返回的列表是共享的，下次调用会被覆盖
        /// </summary>
        public IReadOnlyList<(QuestCondition condition, int current, int target, bool completed)> GetConditionProgressCached(string questId)
        {
            GetConditionProgressNonAlloc(questId, _conditionProgressBuffer);
            return _conditionProgressBuffer;
        }

        #endregion

        #region Helpers

        public QuestConfigSO GetConfig(string id)
        {
            if (_questConfigs.TryGetValue(id, out var config)) return config;
            ZeroLog.Error(ZeroLog.Modules.Quest, $"Config not found: {id}");
            return null;
        }

        private QuestRuntimeData FindActiveQuest(string id)
        {
            for (int i = 0; i < _saveData.activeQuests.Count; i++)
            {
                if (_saveData.activeQuests[i].questId == id)
                    return _saveData.activeQuests[i];
            }
            return null;
        }

        private QuestHistoryData FindHistory(string id)
        {
            for (int i = 0; i < _saveData.history.Count; i++)
            {
                if (_saveData.history[i].questId == id)
                    return _saveData.history[i];
            }
            return null;
        }

        public bool HasActiveQuest(string id)
        {
            for (int i = 0; i < _saveData.activeQuests.Count; i++)
            {
                var q = _saveData.activeQuests[i];
                if (q.questId == id && q.state != QuestState.TheEnd)
                    return true;
            }
            return false;
        }

        public QuestState GetQuestState(string id)
        {
            for (int i = 0; i < _saveData.activeQuests.Count; i++)
            {
                if (_saveData.activeQuests[i].questId == id)
                    return _saveData.activeQuests[i].state;
            }

            for (int i = 0; i < _saveData.history.Count; i++)
            {
                if (_saveData.history[i].questId == id)
                    return QuestState.TheEnd;
            }

            return QuestState.Inactive;
        }

        public int GetQuestCompletionCount(string id)
        {
            for (int i = 0; i < _saveData.history.Count; i++)
            {
                if (_saveData.history[i].questId == id)
                    return _saveData.history[i].completionCount;
            }
            return 0;
        }

        public List<QuestRuntimeData> GetActiveQuests()
        {
            return _saveData.activeQuests;
        }

        public QuestStateSnapshot GetQuestStateSnapshot(string questId)
        {
            var runtime = FindActiveQuest(questId);
            var config = GetConfig(questId);
            return QuestSnapshotFactory.Create(runtime, config);
        }

        public List<QuestStateSnapshot> GetActiveQuestSnapshots()
        {
            var result = new List<QuestStateSnapshot>(_saveData.activeQuests.Count);
            foreach (var quest in _saveData.activeQuests)
            {
                result.Add(QuestSnapshotFactory.Create(quest, GetConfig(quest.questId)));
            }
            return result;
        }

        /// <summary>
        /// 获取指定生命周期的活跃任务 (v1.3.0+)
        /// </summary>
        public List<QuestRuntimeData> GetActiveQuests(QuestLifecycle lifecycle)
        {
            var result = new List<QuestRuntimeData>();
            foreach (var quest in _saveData.activeQuests)
            {
                var config = GetConfig(quest.questId);
                if (config != null && config.lifecycle == lifecycle)
                    result.Add(quest);
            }
            return result;
        }

        /// <summary>
        /// 清除所有 PerRun 生命周期的任务 (v1.3.0+)
        /// </summary>
        public void ClearPerRunQuests()
        {
            for (int i = _saveData.activeQuests.Count - 1; i >= 0; i--)
            {
                var quest = _saveData.activeQuests[i];
                var config = GetConfig(quest.questId);
                if (config != null && config.lifecycle == QuestLifecycle.PerRun)
                {
                    _saveData.activeQuests.RemoveAt(i);
                    ZeroLog.Info(ZeroLog.Modules.Quest, $"Cleared PerRun quest: {quest.questId}");
                    EventManager.Trigger(QuestEvents.QuestAbandoned, quest.questId);
                }
            }
        }

        public QuestSystemSaveData GetSaveData() => _saveData;

        public void LoadSaveData(QuestSystemSaveData data)
        {
            _saveData = data ?? new QuestSystemSaveData();
        }

        #endregion
    }
}
