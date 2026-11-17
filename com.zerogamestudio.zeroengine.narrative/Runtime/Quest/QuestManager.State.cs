using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.Quest
{
    public partial class QuestManager
    {
        private bool _automaticActivationEnabled;
        private bool _activatingQuests;
        private readonly HashSet<string> _activationClaims = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Call after both configuration and the current slot are loaded. Repeat safely after a run reset.
        /// Loading another save suspends activation until its owner calls this again.
        /// </summary>
        public int ActivateConfiguredQuests()
        {
            _automaticActivationEnabled = true;
            if (_activatingQuests) return 0;
            _activatingQuests = true;
            _activationClaims.Clear();
            var count = 0;
            try
            {
                bool changed;
                do
                {
                    changed = false;
                    foreach (var pending in _saveData.activeQuests.ToArray())
                    {
                        if (!_automaticActivationEnabled) return count;
                        if (!_saveData.activeQuests.Contains(pending) ||
                            !_questConfigs.TryGetValue(pending.questId, out var pendingConfig) ||
                            !ValidateDefinition(pendingConfig, out _)) continue;
                        if (pending.state == QuestState.Active) CheckCompletion(pending, pendingConfig);
                        if (!_automaticActivationEnabled) return count;
                        if (pending.state != QuestState.Successful || _activationClaims.Contains(pending.instanceId) ||
                            !pendingConfig.autoSubmit)
                            continue;
                        if (TrySubmitQuest(pending.questId).Succeeded) changed = true;
                    }
                    var candidates = _questConfigs.Values.Where(config => config && config.autoActivate)
                        .OrderBy(config => config.questId, StringComparer.Ordinal).ToArray();
                    foreach (var config in candidates)
                    {
                        if (!_automaticActivationEnabled) return count;
                        if (!CanAcceptQuest(config.questId, out _)) continue;
                        if (!AcceptQuest(config.questId)) continue;
                        count++;
                        changed = true;
                    }
                } while (changed && _automaticActivationEnabled);
                return count;
            }
            finally { _activatingQuests = false; _activationClaims.Clear(); }
        }

        private static bool ValidateDefinition(QuestConfigSO config, out string reason)
        {
            try { return ValidateDefinitionCore(config, out reason); }
            catch (Exception exception) { reason = exception.Message; return false; }
        }

        private static bool ValidateDefinitionCore(QuestConfigSO config, out string reason)
        {
            reason = string.Empty;
            if (config.HasLegacyLogic || config.HasLegacyRewards)
            {
                reason = "Legacy completion/failure or reward fields require explicit migration.";
                return false;
            }
            if (config.autoActivate && (config.repetitionLimit != 1 || (config.Conditions?.Count ?? 0) == 0))
            {
                reason = "Auto-activated quests require Conditions and repetitionLimit=1 (per lifecycle).";
                return false;
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (config.Conditions != null)
                foreach (var condition in config.Conditions)
                {
                    if (condition == null || condition.GetTargetProgress() <= 0 ||
                        string.IsNullOrWhiteSpace(condition.GetStorageKey()) || !keys.Add(condition.GetStorageKey()))
                    {
                        reason = "Null, invalid or duplicate condition progress identities.";
                        return false;
                    }
                }
            keys.Clear();
            if (config.Rewards != null)
                for (var i = 0; i < config.Rewards.Count; i++)
                {
                    var reward = config.Rewards[i];
                    if (reward == null || !keys.Add(GetRewardReceiptKey(reward, i)))
                    {
                        reason = "Null rewards or duplicate reward identities.";
                        return false;
                    }
                }
            return true;
        }

        private static QuestSystemSaveData CloneSaveData(QuestSystemSaveData source)
        {
            var result = new QuestSystemSaveData();
            if (source == null) return result;
            if (source.schemaVersion < 0 || source.schemaVersion > QuestSystemSaveData.CurrentSchemaVersion)
                throw new NotSupportedException($"Unsupported quest save schema: {source.schemaVersion}");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in source.activeQuests ?? new List<QuestRuntimeData>())
            {
                if (quest == null || string.IsNullOrWhiteSpace(quest.questId) || !ids.Add(quest.questId.Trim()) ||
                    !Enum.IsDefined(typeof(QuestState), quest.state))
                    throw new ArgumentException("Invalid or duplicate active quest in save data.");
                var copy = new QuestRuntimeData(quest.questId.Trim())
                {
                    state = quest.state,
                    instanceId = string.IsNullOrEmpty(quest.instanceId) ? Guid.NewGuid().ToString("N") : quest.instanceId,
                    rewardManifest = quest.rewardManifest ?? string.Empty,
                    Progress = quest.Progress == null ? new Dictionary<string, int>() : new Dictionary<string, int>(quest.Progress),
                    grantedRewardIds = quest.grantedRewardIds == null ? new List<string>() : new List<string>(quest.grantedRewardIds)
                };
                if (copy.Progress.Any(pair => string.IsNullOrEmpty(pair.Key) || pair.Value < 0) ||
                    copy.grantedRewardIds.Any(string.IsNullOrWhiteSpace) ||
                    copy.grantedRewardIds.Distinct(StringComparer.Ordinal).Count() != copy.grantedRewardIds.Count)
                    throw new ArgumentException("Invalid progress or reward receipts in quest save data.");
                result.activeQuests.Add(copy);
            }
            ids.Clear();
            foreach (var history in source.history ?? new List<QuestHistoryData>())
            {
                if (history == null || string.IsNullOrWhiteSpace(history.questId) ||
                    !ids.Add(history.questId.Trim()) || history.completionCount < 0)
                    throw new ArgumentException("Invalid or duplicate quest history.");
                result.history.Add(new QuestHistoryData
                {
                    questId = history.questId.Trim(), completionCount = history.completionCount
                });
            }
            result.runBaselinesInitialized = source.runBaselinesInitialized;
            result.runCompletionBaselines = source.runCompletionBaselines == null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(source.runCompletionBaselines);
            foreach (var baseline in result.runCompletionBaselines)
            {
                var total = result.history.FirstOrDefault(history => history.questId == baseline.Key)?.completionCount ?? 0;
                if (string.IsNullOrWhiteSpace(baseline.Key) || baseline.Value < 0 || baseline.Value > total)
                    throw new ArgumentException("Invalid per-run completion baseline.");
            }
            return result;
        }

        private void MigrateConditionProgressForConfig(QuestConfigSO config)
        {
            var quest = FindActiveQuest(config.questId.Trim());
            if (quest == null) return;
            var candidate = new Dictionary<string, int>(quest.Progress);
            MigrateConditionProgress(candidate, config);
            quest.Progress = candidate;
        }

        private static void MigrateConditionProgress(QuestRuntimeData quest, QuestConfigSO config) =>
            MigrateConditionProgress(quest.Progress, config);

        private static void MigrateConditionProgress(Dictionary<string, int> progress, QuestConfigSO config)
        {
            if (config.Conditions == null) return;
            var groups = config.Conditions.Where(condition => condition != null)
                .GroupBy(condition => condition.GetProgressKey(), StringComparer.Ordinal);
            foreach (var group in groups)
            {
                if (!progress.TryGetValue(group.Key, out var value) || value == 0) continue;
                var changed = group.Where(condition => condition.GetStorageKey() != group.Key).ToArray();
                if (changed.Length == 0) continue;
                if (group.Count() != 1)
                    throw new InvalidOperationException($"Ambiguous legacy progress '{group.Key}' in '{config.questId}'.");
                var key = changed[0].GetStorageKey();
                if (progress.TryGetValue(key, out var existing) && existing != value)
                    throw new InvalidOperationException($"Conflicting progress identities in '{config.questId}'.");
                progress[key] = value;
                progress.Remove(group.Key);
            }
        }
    }
}
