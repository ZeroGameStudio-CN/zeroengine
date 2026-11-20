using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Core;
using ZeroEngine.Inventory;
using ZeroEngine.Quest.Editor;
using Object = UnityEngine.Object;

namespace ZeroEngine.Quest.Tests.Editor
{
    // Permanent regressions for reward delivery, persisted identities and UI-independent activation.
    public class QuestReliabilityTests
    {
        private readonly List<Object> objects = new List<Object>();
        private QuestManager manager;
        private DeliveryService service;

        [SetUp]
        public void SetUp()
        {
            foreach (var existing in Object.FindObjectsByType<QuestManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(existing.gameObject);
            EventManager.Clear();
            QuestServiceRegistry.ResetForTests();
            service = new DeliveryService();
            QuestServiceRegistry.SetRewardService(service);
            manager = NewManager();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = objects.Count - 1; i >= 0; i--)
                if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            EventManager.Clear();
            QuestServiceRegistry.ResetForTests();
        }

        [Test]
        public void FailedRewardKeepsClaimableStateAndRetrySkipsDeliveredItems()
        {
            var quest = CreateQuest("claim", "first", "second");
            Complete(quest);
            service.FailId = "second";
            Assert.AreEqual(QuestSubmissionStatus.RewardFailed, manager.TrySubmitQuest("claim").Status);
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState("claim"));
            Assert.AreEqual(0, manager.GetQuestCompletionCount("claim"));
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(0, service.Deliveries("second"));
            service.FailId = null;
            Assert.True(manager.TrySubmitQuest("claim").Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(1, service.Deliveries("second"));
            Assert.AreEqual(1, manager.GetQuestCompletionCount("claim"));
            Assert.AreEqual(QuestSubmissionStatus.NotReady, manager.TrySubmitQuest("claim").Status);
        }

        [Test]
        public void ReceiptSnapshotSurvivesManagerRecreationAndSaveLoad()
        {
            var quest = CreateQuest("resume", "first", "second");
            Complete(quest);
            service.FailId = "second";
            manager.TrySubmitQuest(quest.questId);
            var snapshot = manager.GetSaveData();
            var instance = snapshot.activeQuests.Single().instanceId;
            Assert.AreEqual("id:first", snapshot.activeQuests.Single().grantedRewardIds.Single());
            Object.DestroyImmediate(manager.gameObject);
            manager = NewManager();
            manager.RegisterConfig(quest);
            manager.LoadSaveData(snapshot);
            service.FailId = null;
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(1, service.Deliveries("second"));
            Assert.True(service.Contexts.All(context => context.InstanceId == instance));
        }

        [Test]
        public void ThrownRewardKeepsEarlierReceiptsForRetry()
        {
            var quest = CreateQuest("exception", "first", "second");
            Complete(quest);
            service.ThrowId = "second";
            Assert.AreEqual(QuestSubmissionStatus.RewardFailed, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
            service.ThrowId = null;
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
        }

        [Test]
        public void ReentrantSubmissionDoesNotDuplicateRewards()
        {
            var quest = CreateQuest("reentrant", "first");
            Complete(quest);
            QuestSubmissionResult nested = default;
            service.BeforeGrant = _ => nested = manager.TrySubmitQuest(quest.questId);
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(QuestSubmissionStatus.InProgress, nested.Status);
            Assert.AreEqual(1, service.Deliveries("first"));
        }

        [Test]
        public void RepeatableQuestGetsANewGrantIdentityForEachCompletion()
        {
            var quest = CreateQuest("repeat", "gift");
            quest.repetitionLimit = 0;
            Complete(quest);
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.True(manager.AcceptQuest(quest.questId));
            Hit();
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(2, service.Deliveries("gift"));
            Assert.AreEqual(2, service.Contexts.Select(context => context.ReceiptId).Distinct().Count());
        }

        [Test]
        public void AutoActivationNeedsReadinessButNoQuestUi()
        {
            var quest = CreateQuest("automatic");
            quest.autoActivate = true;
            quest.autoSubmit = true;
            manager.RegisterConfig(quest);
            Assert.AreEqual(QuestState.Inactive, manager.GetQuestState(quest.questId));
            Assert.AreEqual(1, manager.ActivateConfiguredQuests());
            Assert.AreEqual(0, manager.ActivateConfiguredQuests());
            Hit();
            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState(quest.questId));
            Assert.AreEqual(0, manager.ActivateConfiguredQuests());
        }

        [Test]
        public void AutoActivationHonorsPrerequisitesAndBroadcastBoundary()
        {
            var parent = CreateQuest("parent");
            var child = CreateQuest("child");
            parent.autoActivate = child.autoActivate = true;
            parent.autoSubmit = child.autoSubmit = true;
            child.AcceptRequirements.Add(new QuestStateAcceptRequirement { questId = "parent", requiredState = QuestState.TheEnd });
            manager.RegisterConfigs(new[] { parent, child });
            Assert.AreEqual(1, manager.ActivateConfiguredQuests());
            Hit();
            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState("parent"));
            Assert.AreEqual(QuestState.Active, manager.GetQuestState("child"));
            Assert.AreEqual(0, child.Conditions[0].GetCurrentProgress(manager.GetActiveQuests().Single()));
            Hit();
            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState("child"));
        }

        [Test]
        public void LoadingAnotherSlotSuspendsAutomaticActivation()
        {
            var quest = CreateQuest("automatic");
            quest.autoActivate = true;
            manager.RegisterConfig(quest);
            manager.ActivateConfiguredQuests();
            manager.LoadSaveData(new QuestSystemSaveData());
            var another = CreateQuest("another");
            another.autoActivate = true;
            manager.RegisterConfig(another);
            Assert.IsEmpty(manager.GetActiveQuests());
            Assert.AreEqual(2, manager.ActivateConfiguredQuests());
        }

        [Test]
        public void UnlimitedAutomaticQuestIsRejectedInsteadOfLooping()
        {
            var quest = CreateQuest("invalid_auto");
            quest.autoActivate = true;
            quest.repetitionLimit = 0;
            manager.RegisterConfig(quest);
            Assert.AreEqual(0, manager.ActivateConfiguredQuests());
            Assert.False(manager.CanAcceptQuest(quest.questId, out var reason));
            StringAssert.Contains("repetitionLimit", reason);
        }

        [Test]
        public void PerRunQuotaResetsWithoutErasingLifetimeHistory()
        {
            var quest = CreateQuest("per_run", "gift");
            quest.lifecycle = QuestLifecycle.PerRun;
            quest.autoActivate = quest.autoSubmit = true;
            manager.RegisterConfig(quest);
            manager.LoadSaveData(new QuestSystemSaveData
            {
                history = new List<QuestHistoryData> { new QuestHistoryData { questId = "persistent", completionCount = 3 } }
            });
            manager.ActivateConfiguredQuests();
            Hit();
            manager.ClearPerRunQuests();
            Assert.AreEqual(1, manager.GetQuestCompletionCount(quest.questId));
            Assert.AreEqual(3, manager.GetQuestCompletionCount("persistent"));
            Assert.AreEqual(1, manager.ActivateConfiguredQuests());
            Hit();
            Assert.AreEqual(2, service.Deliveries("gift"));
            Assert.AreEqual(2, manager.GetQuestCompletionCount(quest.questId));
            Assert.AreEqual(2, service.Contexts.Select(context => context.ReceiptId).Distinct().Count());
        }

        [Test]
        public void ConditionsSharingATargetHaveIndependentProgress()
        {
            var quest = CreateQuest("independent");
            quest.Conditions.Add(new CustomCondition
            {
                ConditionId = "second", EventType = "Hit", TargetId = "Boss", RequiredCount = 2
            });
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            Hit();
            var runtime = manager.GetActiveQuests().Single();
            Assert.AreEqual(1, quest.Conditions[0].GetCurrentProgress(runtime));
            Assert.AreEqual(1, quest.Conditions[1].GetCurrentProgress(runtime));
            Assert.AreEqual(QuestState.Active, runtime.state);
            Hit();
            Assert.AreEqual(QuestState.Successful, runtime.state);
        }

        [Test]
        public void DuplicateLegacyProgressIdentitiesAreRejected()
        {
            var quest = CreateQuest("duplicate");
            quest.Conditions[0].ConditionId = null;
            quest.Conditions.Add(new CustomCondition { EventType = "Hit", TargetId = "Boss", RequiredCount = 2 });
            manager.RegisterConfig(quest);
            Assert.False(manager.CanAcceptQuest(quest.questId, out var reason));
            StringAssert.Contains("duplicate", reason);
        }

        [Test]
        public void OldSaveWithoutStableIdsKeepsItsProgress()
        {
            var quest = CreateQuest("legacy");
            quest.Conditions[0].ConditionId = null;
            ((CustomCondition)quest.Conditions[0]).RequiredCount = 2;
            manager.RegisterConfig(quest);
            var old = LegacySave(quest.questId);
            manager.LoadSaveData(old);
            Hit();
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
            Assert.AreEqual(1, manager.GetSaveData().schemaVersion);
            Assert.AreEqual(1, old.activeQuests[0].Progress["Custom_Hit_Boss"]);
            Assert.IsNull(old.activeQuests[0].instanceId);
        }

        [Test]
        public void AddingAStableIdMigratesAnUnambiguousLegacyCounter()
        {
            var quest = CreateQuest("migrate");
            ((CustomCondition)quest.Conditions[0]).RequiredCount = 2;
            manager.RegisterConfig(quest);
            manager.LoadSaveData(LegacySave(quest.questId));
            var data = manager.GetSaveData();
            Assert.AreEqual(1, data.activeQuests[0].Progress["condition:objective"]);
            Assert.False(data.activeQuests[0].Progress.ContainsKey("Custom_Hit_Boss"));
            Hit();
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
        }

        [Test]
        public void AmbiguousLegacyCountersAreNotGuessedOrOverwritten()
        {
            var quest = CreateQuest("ambiguous");
            quest.Conditions.Add(new CustomCondition { ConditionId = "second", EventType = "Hit", TargetId = "Boss" });
            manager.RegisterConfig(quest);
            var old = LegacySave(quest.questId);
            Assert.Throws<InvalidOperationException>(() => manager.LoadSaveData(old));
            Assert.IsEmpty(manager.GetActiveQuests());
            Assert.AreEqual(1, old.activeQuests[0].Progress["Custom_Hit_Boss"]);
        }

        [Test]
        public void CompletedConditionDoesNotPublishUnchangedProgress()
        {
            var quest = CreateQuest("no_op");
            quest.Conditions.Add(new CustomCondition
            {
                ConditionId = "other", EventType = "Hit", TargetId = "Other", RequiredCount = 2
            });
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            var notifications = 0;
            manager.OnConditionProgress += (_, _) => notifications++;
            Hit();
            Hit();
            Hit();
            Assert.AreEqual(1, notifications);
        }

        [Test]
        public void CounterClampsWithoutOverflowAndRejectsNegativeAmounts()
        {
            var runtime = new QuestRuntimeData("counter");
            runtime.Progress["key"] = int.MaxValue - 1;
            Assert.True(runtime.TryAddProgress("key", int.MaxValue, int.MaxValue));
            Assert.AreEqual(int.MaxValue, runtime.GetProgress("key"));
            Assert.False(runtime.TryAddProgress("key", -1, int.MaxValue));
        }

        [Test]
        public void NewerOrCorruptSaveDoesNotReplaceHealthyState()
        {
            var quest = CreateQuest("healthy");
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            Assert.Throws<NotSupportedException>(() => manager.LoadSaveData(new QuestSystemSaveData { schemaVersion = 99 }));
            var corrupt = new QuestSystemSaveData
            {
                activeQuests = new List<QuestRuntimeData> { new QuestRuntimeData("same"), new QuestRuntimeData("same") }
            };
            Assert.Throws<ArgumentException>(() => manager.LoadSaveData(corrupt));
            Assert.AreEqual(QuestState.Active, manager.GetQuestState("healthy"));
        }

        [Test]
        public void ExportedSnapshotDoesNotAliasLiveProgress()
        {
            var quest = CreateQuest("snapshot");
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            var snapshot = manager.GetSaveData();
            snapshot.activeQuests[0].Progress["condition:objective"] = 999;
            snapshot.activeQuests.Clear();
            Assert.AreEqual(1, manager.GetActiveQuests().Count);
            Assert.AreEqual(0, quest.Conditions[0].GetCurrentProgress(manager.GetActiveQuests()[0]));
        }

        [Test]
        public void ExplicitRewardIdsAllowReorderingAPendingClaim()
        {
            var quest = CreateQuest("reorder", "first", "second");
            Complete(quest);
            service.FailId = "second";
            manager.TrySubmitQuest(quest.questId);
            quest.Rewards.Reverse();
            service.FailId = null;
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
        }

        [Test]
        public void ChangedPendingRewardPayloadIsRejectedBeforeFurtherDelivery()
        {
            var quest = CreateQuest("changed", "first", "second");
            Complete(quest);
            service.FailId = "second";
            manager.TrySubmitQuest(quest.questId);
            ((TestReward)quest.Rewards[1]).Payload++;
            service.FailId = null;
            Assert.AreEqual(QuestSubmissionStatus.InvalidConfig, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(0, service.Deliveries("second"));
        }

        [Test]
        public void SlotSwitchDuringGrantCannotCommitIntoTheNewSlot()
        {
            var quest = CreateQuest("switch", "gift");
            Complete(quest);
            service.BeforeGrant = _ => manager.LoadSaveData(new QuestSystemSaveData());
            Assert.AreEqual(QuestSubmissionStatus.StateChanged, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(0, manager.GetQuestCompletionCount(quest.questId));
            Assert.IsEmpty(manager.GetActiveQuests());
        }

        [Test]
        public void NullRewardIsRejectedBeforeAnyDelivery()
        {
            var quest = CreateQuest("null", "gift");
            Complete(quest);
            quest.Rewards.Add(null);
            Assert.AreEqual(QuestSubmissionStatus.InvalidConfig, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(0, service.Deliveries("gift"));
        }

        [Test]
        public void LegacyRewardServiceStillReceivesBooleanGrantCalls()
        {
            var quest = CreateQuest("old_service", "gift");
            Complete(quest);
            var legacy = new LegacyService();
            QuestServiceRegistry.SetRewardService(legacy);
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, legacy.Calls);
        }

        [Test]
        public void LegacyFieldsAreValidatedAndCanBeExplicitlyMigrated()
        {
            var quest = CreateQuest("old_reward");
            quest.goldReward = 12;
            manager.RegisterConfig(quest);
            Assert.False(manager.CanAcceptQuest(quest.questId, out _));
            Assert.True(QuestConfigValidator.Validate(new[] { quest }).Any(issue => issue.Severity == QuestValidationSeverity.Error));
            typeof(QuestConfigSO).GetMethod("MigrateLegacyRewards", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(quest, null);
            Assert.AreEqual(0, quest.goldReward);
            Assert.AreEqual(12, ((CurrencyReward)quest.Rewards.Single()).Amount);
            Assert.True(manager.CanAcceptQuest(quest.questId, out _));
        }

        [Test]
        public void ValidatorFindsDuplicateIdsAndProgressKeys()
        {
            var first = CreateQuest("duplicate_id");
            var second = CreateQuest("duplicate_id");
            first.Conditions[0].ConditionId = null;
            first.Conditions.Add(new CustomCondition { EventType = "Hit", TargetId = "Boss" });
            var issues = QuestConfigValidator.Validate(new[] { first, second });
            Assert.True(issues.Any(issue => issue.Message == "Duplicate quest ID"));
            Assert.True(issues.Any(issue => issue.Message == "Duplicate condition progress identity"));
        }

        [Test]
        public void ReadinessResumesAPendingAutomaticClaimAfterReload()
        {
            var quest = CreateQuest("auto_resume", "first", "second");
            quest.autoActivate = quest.autoSubmit = true;
            service.FailId = "second";
            manager.RegisterConfig(quest);
            manager.ActivateConfiguredQuests();
            Hit();
            var snapshot = manager.GetSaveData();
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
            manager.LoadSaveData(snapshot);
            service.FailId = null;
            manager.ActivateConfiguredQuests();
            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState(quest.questId));
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(1, service.Deliveries("second"));
        }

        [Test]
        public void AutomaticFailureIsRetriedOnlyOnceInAnActivationPass()
        {
            var quest = CreateQuest("auto_failure", "gift");
            quest.autoSubmit = true;
            service.FailId = "gift";
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            Hit();
            var another = CreateQuest("another");
            another.autoActivate = true;
            manager.RegisterConfig(another);
            var before = service.Contexts.Count;
            manager.ActivateConfiguredQuests();
            Assert.AreEqual(1, service.Contexts.Count - before);
        }

        [Test]
        public void ProviderAvailabilityUsesTheSamePrerequisiteCheck()
        {
            var quest = CreateQuest("provider");
            quest.AcceptRequirements.Add(new QuestStateAcceptRequirement { questId = "prerequisite" });
            manager.RegisterConfig(quest);
            var go = new GameObject("Provider");
            objects.Add(go);
            var provider = go.AddComponent<QuestProvider>();
            provider.quests = new List<QuestConfigSO> { quest };
            Assert.False(provider.HasQuestForPlayer());
            manager.LoadSaveData(new QuestSystemSaveData
            {
                history = new List<QuestHistoryData> { new QuestHistoryData { questId = "prerequisite", completionCount = 1 } }
            });
            Assert.True(provider.HasQuestForPlayer());
        }

        [Test]
        public void LegacyRewardPositionsCannotBeReorderedDuringAPartialClaim()
        {
            var quest = CreateQuest("legacy_positions", "first", "second");
            foreach (var reward in quest.Rewards) reward.RewardId = null;
            var calls = 0;
            QuestServiceRegistry.SetRewardService(new ControlledLegacyService(() => ++calls == 1));
            Complete(quest);
            manager.TrySubmitQuest(quest.questId);
            ((TestReward)quest.Rewards[1]).Payload = 2;
            quest.Rewards.Reverse();
            Assert.AreEqual(QuestSubmissionStatus.InvalidConfig, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(2, calls);
        }

        private sealed class ControlledLegacyService : IQuestRewardService
        {
            private readonly Func<bool> grant;
            public ControlledLegacyService(Func<bool> grant) { this.grant = grant; }
            public bool Grant(QuestReward reward, QuestConfigSO quest) => grant();
        }

        [Test]
        public void ItemAssetInstanceChangesDoNotInvalidatePendingClaims()
        {
            var quest = CreateQuest("asset_reload", "first");
            var item = ScriptableObject.CreateInstance<InventoryItemSO>();
            objects.Add(item);
            item.Id = "stable_item";
            var reward = new ItemReward { RewardId = "second", ItemId = item.Id, ItemData = item, Quantity = 2 };
            quest.Rewards.Add(reward);
            Complete(quest);
            service.FailId = "second";
            manager.TrySubmitQuest(quest.questId);
            Object.DestroyImmediate(item);
            var replacement = ScriptableObject.CreateInstance<InventoryItemSO>();
            objects.Add(replacement);
            replacement.Id = "stable_item";
            reward.ItemData = replacement;
            service.FailId = null;
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(1, service.Deliveries("second"));
        }

        [Test]
        public void SerializedReceiptAndProgressRoundTripBeforeRetry()
        {
            var quest = CreateQuest("serialized", "first", "second");
            Complete(quest);
            service.FailId = "second";
            manager.TrySubmitQuest(quest.questId);
            var snapshot = manager.GetSaveData();
            var json = JsonUtility.ToJson(snapshot);
            var restored = JsonUtility.FromJson<QuestSystemSaveData>(json);
            Assert.AreEqual(snapshot.activeQuests[0].instanceId, restored.activeQuests[0].instanceId);
            Assert.AreEqual(1, restored.activeQuests[0].Progress["condition:objective"]);
            Assert.AreEqual("id:first", restored.activeQuests[0].grantedRewardIds.Single());
            manager.LoadSaveData(restored);
            service.FailId = null;
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(1, service.Deliveries("first"));
        }

        [Test]
        public void DefaultSubmissionResultCannotReportSuccess()
        {
            Assert.False(default(QuestSubmissionResult).Succeeded);
        }

        [Test]
        public void InvalidGrantFingerprintDoesNotEscapeOrDeliverRewards()
        {
            var quest = CreateQuest("bad_fingerprint", "first");
            Complete(quest);
            quest.Rewards.Add(new InvalidFingerprintReward { RewardId = "invalid" });
            Assert.AreEqual(QuestSubmissionStatus.InvalidConfig, manager.TrySubmitQuest(quest.questId).Status);
            Assert.AreEqual(0, service.Deliveries("first"));
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
        }

        [Serializable]
        private sealed class InvalidFingerprintReward : QuestReward
        {
            public override string RewardType => "Invalid";
            public override bool Grant() => true;
            public override string GetPreviewText() => string.Empty;
            public override string GetGrantFingerprint() => throw new InvalidOperationException("Invalid authored definition");
        }

        [Test]
        public void LoadedCompletedProgressCanFinishWithoutAnotherGameplayEvent()
        {
            var quest = CreateQuest("loaded_complete", "gift");
            quest.autoActivate = quest.autoSubmit = true;
            manager.RegisterConfig(quest);
            manager.LoadSaveData(new QuestSystemSaveData
            {
                activeQuests = new List<QuestRuntimeData>
                {
                    new QuestRuntimeData(quest.questId)
                    {
                        state = QuestState.Active,
                        Progress = new Dictionary<string, int> { ["condition:objective"] = 1 }
                    }
                }
            });
            manager.ActivateConfiguredQuests();
            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState(quest.questId));
            Assert.AreEqual(1, service.Deliveries("gift"));
        }

        [Test]
        public void SwitchingSlotsDuringProgressStopsOldCompletionNotifications()
        {
            var quest = CreateQuest("progress_switch");
            manager.RegisterConfig(quest);
            manager.AcceptQuest(quest.questId);
            var completed = 0;
            manager.OnConditionProgress += (_, _) => manager.LoadSaveData(new QuestSystemSaveData());
            manager.OnConditionCompleted += (_, _) => completed++;
            Hit();
            Assert.AreEqual(0, completed);
            Assert.AreEqual(QuestState.Inactive, manager.GetQuestState(quest.questId));
            Assert.AreEqual(0, manager.GetQuestCompletionCount(quest.questId));
        }

        [Test]
        public void LegacyPerRunQuotaIsConservativeUntilAnExplicitRunBoundary()
        {
            var quest = CreateQuest("old_per_run");
            quest.lifecycle = QuestLifecycle.PerRun;
            manager.RegisterConfig(quest);
            manager.LoadSaveData(new QuestSystemSaveData
            {
                schemaVersion = 0,
                history = new List<QuestHistoryData>
                {
                    new QuestHistoryData { questId = quest.questId, completionCount = 5 }
                }
            });
            Assert.False(manager.CanAcceptQuest(quest.questId, out _));
            manager.ClearPerRunQuests();
            var serialized = JsonUtility.ToJson(manager.GetSaveData());
            manager.LoadSaveData(JsonUtility.FromJson<QuestSystemSaveData>(serialized));
            Assert.AreEqual(5, manager.GetQuestCompletionCount(quest.questId));
            Assert.True(manager.AcceptQuest(quest.questId));
            Hit();
            Assert.True(manager.TrySubmitQuest(quest.questId).Succeeded);
            Assert.AreEqual(6, manager.GetQuestCompletionCount(quest.questId));
            Assert.False(manager.CanAcceptQuest(quest.questId, out _));
        }

        [Test]
        public void SwitchingSlotsInRewardReceiptCallbackStopsRemainingGrants()
        {
            var quest = CreateQuest("receipt_slot_switch", "first", "second");
            Complete(quest);
            manager.OnRewardGranted += _ => manager.LoadSaveData(new QuestSystemSaveData());

            var result = manager.TrySubmitQuest(quest.questId);

            Assert.AreEqual(QuestSubmissionStatus.StateChanged, result.Status);
            Assert.AreEqual(1, service.Deliveries("first"));
            Assert.AreEqual(0, service.Deliveries("second"),
                "A receipt callback can switch the recipient slot; later rewards must not be dispatched.");
            Assert.IsEmpty(manager.GetActiveQuests());
            Assert.AreEqual(0, manager.GetQuestCompletionCount(quest.questId));
        }

        private QuestManager NewManager()
        {
            var go = new GameObject("QuestReliabilityTests");
            objects.Add(go);
            return go.AddComponent<QuestManager>();
        }

        private QuestConfigSO CreateQuest(string id, params string[] rewards)
        {
            var quest = ScriptableObject.CreateInstance<QuestConfigSO>();
            objects.Add(quest);
            quest.questId = id;
            quest.repetitionLimit = 1;
            quest.Conditions.Add(new CustomCondition
            {
                ConditionId = "objective", EventType = "Hit", TargetId = "Boss"
            });
            foreach (var reward in rewards) quest.Rewards.Add(new TestReward { RewardId = reward });
            return quest;
        }

        private void Complete(QuestConfigSO quest)
        {
            manager.RegisterConfig(quest);
            Assert.True(manager.AcceptQuest(quest.questId));
            Hit();
            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(quest.questId));
        }

        private void Hit() => manager.ProcessConditionEvent("Hit", new ConditionEventData("Boss", 1));

        private static QuestSystemSaveData LegacySave(string id) => new QuestSystemSaveData
        {
            schemaVersion = 0,
            activeQuests = new List<QuestRuntimeData>
            {
                new QuestRuntimeData(id)
                {
                    state = QuestState.Active, instanceId = null, grantedRewardIds = null,
                    Progress = new Dictionary<string, int> { ["Custom_Hit_Boss"] = 1 }
                }
            }
        };

        [Serializable]
        private sealed class TestReward : QuestReward
        {
            public int Payload = 1;
            public override string RewardType => "Test";
            public override bool Grant() => true;
            public override string GetPreviewText() => "Test reward";
        }

        private sealed class DeliveryService : IContextualQuestRewardService
        {
            public string FailId;
            public string ThrowId;
            public Action<QuestRewardGrantContext> BeforeGrant;
            public readonly List<QuestRewardGrantContext> Contexts = new List<QuestRewardGrantContext>();
            private readonly HashSet<string> receipts = new HashSet<string>();
            private readonly Dictionary<string, int> deliveries = new Dictionary<string, int>();

            public int Deliveries(string id) => deliveries.TryGetValue(id, out var count) ? count : 0;
            public bool Grant(QuestReward reward, QuestConfigSO quest) => throw new InvalidOperationException("Context required");
            public bool Grant(QuestReward reward, QuestConfigSO quest, QuestRewardGrantContext context)
            {
                Contexts.Add(context);
                BeforeGrant?.Invoke(context);
                if (reward.RewardId == ThrowId) throw new InvalidOperationException("Synthetic delivery failure");
                if (reward.RewardId == FailId) return false;
                if (receipts.Add(context.ReceiptId)) deliveries[reward.RewardId] = Deliveries(reward.RewardId) + 1;
                return true;
            }
        }

        private sealed class LegacyService : IQuestRewardService
        {
            public int Calls;
            public bool Grant(QuestReward reward, QuestConfigSO quest) { Calls++; return true; }
        }
    }
}
