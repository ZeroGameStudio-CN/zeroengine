using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Save;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    public readonly struct QuestCompletionTransition
    {
        public QuestCompletionTransition(long sequence, string questId)
        {
            Sequence = sequence;
            QuestId = questId;
        }

        public long Sequence { get; }

        public string QuestId { get; }
    }

    public class QuestManager : Singleton<QuestManager>, ISaveable
    {
        private QuestSystemSaveData _saveData = new QuestSystemSaveData();
        private Dictionary<string, QuestConfigSO> _questConfigs = new Dictionary<string, QuestConfigSO>();
        private SaveSlotManager _registeredSaveSlotManager;
        private long _completionTransitionSequence;

        #region Events

        /// <summary>
        /// 条件进度更新时触发。
        /// </summary>
        public event Action<string, QuestCondition> OnConditionProgress;

        /// <summary>
        /// 单个条件完成时触发。
        /// </summary>
        public event Action<string, QuestCondition> OnConditionCompleted;

        /// <summary>
        /// Raised once for each Active-to-Successful transition owned by this manager instance.
        /// This channel is independent from the legacy static EventManager lifecycle.
        /// </summary>
        public event Action<QuestCompletionTransition> OnQuestCompletionTransition;

        public long CompletionTransitionSequence => _completionTransitionSequence;

        #endregion

        protected override void Awake()
        {
            base.Awake();
            LoadConfig();
        }

        private void Start()
        {
            RegisterEvents();
            _registeredSaveSlotManager = SaveSlotManager.Instance;
            _registeredSaveSlotManager?.Register(this);
        }

        protected override void OnDestroy()
        {
            UnregisterEvents();
            if (_registeredSaveSlotManager != null)
                _registeredSaveSlotManager.Unregister(this);
            _registeredSaveSlotManager = null;
            base.OnDestroy();
        }

        #region ISaveable Implementation

        public string SaveKey => "Quest";

        public object ExportSaveData()
        {
            return _saveData;
        }

        public void ImportSaveData(object data)
        {
            _saveData = data as QuestSystemSaveData ?? new QuestSystemSaveData();
        }

        public void ResetToDefault()
        {
            _saveData = new QuestSystemSaveData();
        }

        #endregion

        private void LoadConfig()
        {
            ReloadConfigsFromSource();
        }

        /// <summary>
        /// 注册任务配置。
        /// </summary>
        public void RegisterConfig(QuestConfigSO config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.questId))
                return;

            _questConfigs[config.questId.Trim()] = config;
        }

        /// <summary>
        /// Registers multiple quest configs.
        /// </summary>
        public int RegisterConfigs(IEnumerable<QuestConfigSO> configs)
        {
            if (configs == null) return 0;

            var count = 0;
            foreach (var config in configs)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.questId)) continue;

                RegisterConfig(config);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Reloads configs from the registered project source, then Resources fallback if no custom source is registered.
        /// </summary>
        public void ReloadConfigsFromSource()
        {
            _questConfigs.Clear();

            var serviceConfigs = QuestServiceRegistry.ConfigSource.LoadConfigs();
            RegisterConfigs(serviceConfigs);

            if (_questConfigs.Count > 0 || QuestServiceRegistry.HasCustomConfigSource)
                return;

            var resourcesConfigs = Resources.LoadAll<QuestConfigSO>("Quests");
            RegisterConfigs(resourcesConfigs);
        }

        #region Event Management

        private void RegisterEvents()
        {
            EventManager.Subscribe<string, int>(GameEvents.ItemObtained, OnItemObtained);
            EventManager.Subscribe<string, int>(GameEvents.CharacterDied, OnEntityKilled);
            EventManager.Subscribe<string>(GameEvents.QuestProgressChanged, OnManualProgress);

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

        private void OnItemObtained(string itemId, int count)
        {
            ProcessConditionEvent(QuestEvents.ItemObtained, new ConditionEventData(itemId, count));
        }

        private void OnEntityKilled(string entityId, int count)
        {
            ProcessConditionEvent(QuestEvents.EntityKilled, new ConditionEventData(entityId, count));
        }

        private void OnManualProgress(string targetName)
        {
            ProcessConditionEvent(QuestEvents.Interacted, new ConditionEventData(targetName, 1));
        }

        private void OnEntityKilledCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.EntityKilled, data);
        private void OnItemObtainedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.ItemObtained, data);
        private void OnInteractedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.Interacted, data);
        private void OnLocationReachedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.LocationReached, data);
        private void OnSurviveCompletedCondition(ConditionEventData data) => ProcessConditionEvent(QuestEvents.SurviveCompleted, data);

        #endregion

        #region Core Logic

        /// <summary>
        /// 处理条件事件。
        /// </summary>
        public void ProcessConditionEvent(string eventType, ConditionEventData data)
        {
            var activeQuestSnapshot = new List<QuestRuntimeData>(_saveData.activeQuests);
            for (int i = 0; i < activeQuestSnapshot.Count; i++)
            {
                var quest = activeQuestSnapshot[i];
                if (!_saveData.activeQuests.Contains(quest) || quest.state != QuestState.Active) continue;

                var config = GetConfig(quest.questId);
                if (config == null || config.Conditions == null || config.Conditions.Count == 0) continue;

                bool questChanged = false;

                foreach (var condition in config.Conditions)
                {
                    if (condition == null) continue;

                    bool wasCompleted = condition.IsSatisfied(quest);
                    bool updated = condition.ProcessEvent(quest, eventType, data);

                    if (!updated) continue;

                    questChanged = true;
                    OnConditionProgress?.Invoke(quest.questId, condition);

                    if (!wasCompleted && condition.IsSatisfied(quest))
                        OnConditionCompleted?.Invoke(quest.questId, condition);
                }

                if (questChanged)
                    CheckCompletion(quest, config);
            }
        }

        public void UpdateQuestProgress(QuestType type, string targetName, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(targetName)) return;

            amount = Mathf.Max(1, amount);
            var eventType = type switch
            {
                QuestType.Collect => QuestEvents.ItemObtained,
                QuestType.KillMonster => QuestEvents.EntityKilled,
                QuestType.Dialogue => QuestEvents.Interacted,
                _ => QuestEvents.Interacted
            };
            ProcessConditionEvent(eventType, new ConditionEventData(targetName, amount));
        }

        private void CheckCompletion(QuestRuntimeData quest, QuestConfigSO config)
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
                CompleteQuest(quest, config);
        }

        private void CompleteQuest(QuestRuntimeData quest, QuestConfigSO config)
        {
            if (quest == null || quest.state != QuestState.Active) return;

            quest.state = QuestState.Successful;
            var transition = new QuestCompletionTransition(
                ++_completionTransitionSequence,
                quest.questId);
            ZeroLog.Info(ZeroLog.Modules.Quest, $"Quest '{quest.questId}' Conditions Met!");
            OnQuestCompletionTransition?.Invoke(transition);
            EventManager.Trigger(GameEvents.QuestCompleted, quest.questId);

            if (config.autoSubmit)
                SubmitQuest(quest.questId);
        }

        public bool AcceptQuest(string questId)
        {
            if (!CanAcceptQuest(questId, out var reason))
            {
                ZeroLog.Warning(ZeroLog.Modules.Quest, reason);
                return false;
            }

            var normalizedQuestId = questId.Trim();
            var newQuest = new QuestRuntimeData(normalizedQuestId)
            {
                state = QuestState.Active
            };
            _saveData.activeQuests.Add(newQuest);

            ZeroLog.Info(ZeroLog.Modules.Quest, $"Accepted: {normalizedQuestId}");
            EventManager.Trigger(GameEvents.QuestAccepted, normalizedQuestId);
            return true;
        }

        public bool CanAcceptQuest(string questId, out string reason)
        {
            reason = string.Empty;
            var normalizedQuestId = questId?.Trim();
            if (string.IsNullOrEmpty(normalizedQuestId))
            {
                reason = "Quest id is empty.";
                return false;
            }

            if (HasActiveQuest(normalizedQuestId))
            {
                reason = $"Already has active quest: {normalizedQuestId}";
                return false;
            }

            var config = GetConfig(normalizedQuestId);
            if (config == null)
            {
                reason = $"Config not found: {normalizedQuestId}";
                return false;
            }

            if (config.repetitionLimit > 0)
            {
                int doneCount = GetQuestCompletionCount(normalizedQuestId);
                if (doneCount >= config.repetitionLimit)
                {
                    reason = $"Repetition limit reached for: {normalizedQuestId}";
                    return false;
                }
            }

            if (config.AcceptRequirements == null) return true;

            foreach (var requirement in config.AcceptRequirements)
            {
                if (requirement == null) continue;
                if (requirement.IsSatisfied(this, out var blockReason)) continue;

                reason = string.IsNullOrWhiteSpace(blockReason)
                    ? $"Accept requirement failed: {requirement.GetPreviewText()}"
                    : blockReason;
                return false;
            }

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
                GrantRewards(config);

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
        /// 发放奖励。
        /// </summary>
        private void GrantRewards(QuestConfigSO config)
        {
            if (config?.Rewards == null) return;

            foreach (var reward in config.Rewards)
            {
                if (reward == null) continue;
                QuestServiceRegistry.RewardService.Grant(reward, config);
            }
        }

        /// <summary>
        /// 放弃任务。
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

        #region Query Methods

        private readonly List<(QuestCondition condition, int current, int target, bool completed)> _conditionProgressBuffer
            = new List<(QuestCondition, int, int, bool)>(8);

        /// <summary>
        /// 获取任务的条件进度列表。
        /// </summary>
        public List<(QuestCondition condition, int current, int target, bool completed)> GetConditionProgress(string questId)
        {
            var result = new List<(QuestCondition, int, int, bool)>();
            GetConditionProgressNonAlloc(questId, result);
            return result;
        }

        /// <summary>
        /// 获取任务的条件进度列表 (零分配版本)。
        /// </summary>
        public int GetConditionProgressNonAlloc(string questId, List<(QuestCondition condition, int current, int target, bool completed)> buffer)
        {
            buffer.Clear();

            var quest = FindActiveQuest(questId);
            if (quest == null) return 0;

            var config = GetConfig(questId);
            if (config == null || config.Conditions == null || config.Conditions.Count == 0) return 0;

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
        /// 获取任务的条件进度列表 (使用内部缓冲区)。
        /// 注意：返回的列表是共享的，下次调用会被覆盖。
        /// </summary>
        public IReadOnlyList<(QuestCondition condition, int current, int target, bool completed)> GetConditionProgressCached(string questId)
        {
            GetConditionProgressNonAlloc(questId, _conditionProgressBuffer);
            return _conditionProgressBuffer;
        }

        public QuestStateSnapshot GetQuestStateSnapshot(string questId)
        {
            var quest = FindActiveQuest(questId);
            return quest != null
                ? BuildSnapshot(quest)
                : new QuestStateSnapshot(questId, GetQuestState(questId), Array.Empty<QuestObjectiveSnapshot>(), Array.Empty<string>());
        }

        public IReadOnlyList<QuestStateSnapshot> GetActiveQuestSnapshots()
        {
            var result = new List<QuestStateSnapshot>(_saveData.activeQuests.Count);
            for (var i = 0; i < _saveData.activeQuests.Count; i++)
                result.Add(BuildSnapshot(_saveData.activeQuests[i]));
            return result;
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

        /// <summary>
        /// 获取指定生命周期的活跃任务。
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
        /// 清除所有 PerRun 生命周期的任务。
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

        private QuestStateSnapshot BuildSnapshot(QuestRuntimeData quest)
        {
            if (quest == null) return default;

            var config = GetConfig(quest.questId);
            var objectives = new List<QuestObjectiveSnapshot>();
            if (config?.Conditions != null)
            {
                foreach (var condition in config.Conditions)
                {
                    if (condition == null) continue;
                    var key = condition.GetProgressKey();
                    objectives.Add(new QuestObjectiveSnapshot(
                        key,
                        key,
                        string.IsNullOrWhiteSpace(condition.Description) ? key : condition.Description,
                        condition.GetCurrentProgress(quest),
                        condition.GetTargetProgress(),
                        condition.IsSatisfied(quest),
                        condition.IsHidden));
                }
            }

            return new QuestStateSnapshot(
                quest.questId,
                quest.state,
                objectives,
                config != null ? config.GetRewardPreviews() : Array.Empty<string>());
        }

#if UNITY_EDITOR
        public void ForceCompleteQuestForTests(string questId)
        {
            var normalizedQuestId = questId?.Trim();
            if (string.IsNullOrEmpty(normalizedQuestId)) return;

            var quest = FindActiveQuest(normalizedQuestId);
            if (quest == null)
            {
                quest = new QuestRuntimeData(normalizedQuestId);
                _saveData.activeQuests.Add(quest);
            }

            quest.state = QuestState.Successful;
        }
#endif

        #endregion
    }
}
