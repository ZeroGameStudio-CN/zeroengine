using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Utils;

namespace ZeroEngine.Quest
{
    public partial class QuestManager
    {
        private readonly HashSet<string> _submittingQuests = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Save the quest snapshot with the recipient state after each successful reward.</summary>
        public event Action<QuestRewardGrantContext> OnRewardGranted;

        public QuestSubmissionResult TrySubmitQuest(string questId)
        {
            var id = questId?.Trim() ?? string.Empty;
            if (_submittingQuests.Contains(id))
                return new QuestSubmissionResult(QuestSubmissionStatus.InProgress, id);

            var save = _saveData;
            var quest = FindActiveQuest(id);
            if (quest == null || quest.state != QuestState.Successful)
                return new QuestSubmissionResult(QuestSubmissionStatus.NotReady, id);
            if (!_questConfigs.TryGetValue(id, out var config) || !config)
                return new QuestSubmissionResult(QuestSubmissionStatus.MissingConfig, id);
            if (!ValidateDefinition(config, out var reason))
                return new QuestSubmissionResult(QuestSubmissionStatus.InvalidConfig, id, reason);

            var rewards = new List<(string id, QuestReward reward)>();
            if (config.Rewards != null)
                for (var i = 0; i < config.Rewards.Count; i++)
                {
                    var reward = config.Rewards[i];
                    rewards.Add((GetRewardReceiptKey(reward, i), reward));
                }

            // Freeze the authored payload, not list order when explicit reward IDs are present.
            string manifest;
            try
            {
                var manifestEntries = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in rewards)
                    manifestEntries.Add(entry.id, entry.reward.GetType().AssemblyQualifiedName + "\n" + entry.reward.GetGrantFingerprint());
                var manifestText = new StringBuilder();
                foreach (var entry in manifestEntries)
                    manifestText.Append(entry.Key.Length).Append(':').Append(entry.Key)
                        .Append(entry.Value.Length).Append(':').Append(entry.Value);
                manifest = Hash128.Compute(manifestText.ToString()).ToString();
            }
            catch (Exception exception)
            {
                return new QuestSubmissionResult(QuestSubmissionStatus.InvalidConfig, id, exception.Message);
            }
            if (!string.IsNullOrEmpty(quest.rewardManifest) && quest.rewardManifest != manifest)
                return new QuestSubmissionResult(QuestSubmissionStatus.InvalidConfig, id,
                    "Reward definitions changed during a pending claim. An explicit receipt migration is required.");
            quest.rewardManifest = manifest;

            _submittingQuests.Add(id);
            if (_activatingQuests) _activationClaims.Add(quest.instanceId);
            var currentRewardId = string.Empty;
            var committed = false;
            var service = QuestServiceRegistry.RewardService;
            try
            {
                foreach (var entry in rewards)
                {
                    if (!ReferenceEquals(save, _saveData) || !save.activeQuests.Contains(quest) ||
                        quest.state != QuestState.Successful)
                        return new QuestSubmissionResult(QuestSubmissionStatus.StateChanged, id);
                    currentRewardId = entry.id;
                    if (quest.grantedRewardIds.Contains(entry.id)) continue;
                    var context = new QuestRewardGrantContext(id, quest.instanceId, entry.id);
                    var granted = service is IContextualQuestRewardService contextual
                        ? contextual.Grant(entry.reward, config, context)
                        : service.Grant(entry.reward, config);
                    if (!granted)
                        return new QuestSubmissionResult(QuestSubmissionStatus.RewardFailed, id,
                            "The reward service did not confirm delivery.", entry.id);
                    // A callback may have switched slots or removed this quest. Never commit to the new state.
                    if (!ReferenceEquals(save, _saveData) || !save.activeQuests.Contains(quest) ||
                        quest.state != QuestState.Successful)
                        return new QuestSubmissionResult(QuestSubmissionStatus.StateChanged, id);
                    quest.grantedRewardIds.Add(entry.id);
                    OnRewardGranted?.Invoke(context);
                }

                if (!ReferenceEquals(save, _saveData) || !save.activeQuests.Contains(quest) ||
                    quest.state != QuestState.Successful)
                    return new QuestSubmissionResult(QuestSubmissionStatus.StateChanged, id);

                quest.state = QuestState.TheEnd;
                save.activeQuests.Remove(quest);
                var history = FindHistory(id);
                if (history == null)
                {
                    history = new QuestHistoryData { questId = id };
                    save.history.Add(history);
                }
                history.completionCount++;
                committed = true;
                EventManager.Trigger(QuestEvents.QuestSubmitted, id);
                return new QuestSubmissionResult(QuestSubmissionStatus.Submitted, id);
            }
            catch (Exception exception)
            {
                ZeroLog.Warning(ZeroLog.Modules.Quest, $"Reward claim for '{id}' interrupted: {exception.Message}");
                return new QuestSubmissionResult(committed ? QuestSubmissionStatus.Submitted : QuestSubmissionStatus.RewardFailed,
                    id, exception.Message, currentRewardId);
            }
            finally
            {
                _submittingQuests.Remove(id);
                if (committed && ReferenceEquals(save, _saveData) && _automaticActivationEnabled)
                {
                    try { ActivateConfiguredQuests(); }
                    catch (Exception exception) { ZeroLog.Warning(ZeroLog.Modules.Quest, $"Automatic activation interrupted: {exception.Message}"); }
                }
            }
        }

        internal static string GetRewardReceiptKey(QuestReward reward, int index) =>
            string.IsNullOrWhiteSpace(reward.RewardId) ? $"legacy:{index}" : $"id:{reward.RewardId.Trim()}";
    }
}
