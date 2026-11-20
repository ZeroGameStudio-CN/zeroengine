using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Core;
using Object = UnityEngine.Object;

namespace ZeroEngine.Quest.Tests.Editor
{
    // Public-API workflows with disk-backed recipient receipts, isolated from consumer saves.
    public class QuestPersistenceWorkflowTests
    {
        private readonly List<Object> objects = new List<Object>();
        private string directory;
        private QuestManager manager;
        private DiskRewardService service;
        private QuestConfigSO definition;

        [SetUp]
        public void SetUp()
        {
            foreach (var existing in Object.FindObjectsByType<QuestManager>(FindObjectsSortMode.None))
                Object.DestroyImmediate(existing.gameObject);
            EventManager.Clear();
            QuestServiceRegistry.ResetForTests();
            directory = Path.Combine(Application.temporaryCachePath, "QuestDiskWorkflow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            definition = ScriptableObject.CreateInstance<QuestConfigSO>();
            objects.Add(definition);
            definition.questId = "hidden_encounter_unlock";
            definition.repetitionLimit = 1;
            definition.autoActivate = definition.autoSubmit = true;
            definition.Conditions.Add(new CustomCondition
            {
                ConditionId = "defeat_final_encounter", EventType = "EncounterWon",
                TargetId = "final_encounter", RequiredCount = 1
            });
            definition.Rewards.Add(new ContentReward
            {
                RewardId = "hidden_passive", ContentId = "content.hidden_passive"
            });
        }

        [TearDown]
        public void TearDown()
        {
            for (var index = objects.Count - 1; index >= 0; index--)
                if (objects[index]) Object.DestroyImmediate(objects[index]);
            objects.Clear();
            EventManager.Clear();
            QuestServiceRegistry.ResetForTests();
            if (directory != null && Directory.Exists(directory))
            {
                var parent = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase));
                foreach (var file in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    File.Delete(file);
                Directory.Delete(directory, false);
                Assert.False(Directory.Exists(directory));
            }
        }

        [Test]
        public void HiddenUnlockPersistsOnDiskWithoutUiAndStaysIsolatedBetweenSlots()
        {
            Restart("a");
            Assert.AreEqual(QuestState.Inactive, manager.GetQuestState(definition.questId));
            Assert.AreEqual(1, manager.ActivateConfiguredQuests());
            manager.ProcessConditionEvent("EncounterWon", new ConditionEventData("different_encounter"));
            Assert.AreEqual(0, service.ReadLedger().SuccessfulGrants);
            WinEncounter();
            SaveQuest("a");

            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState(definition.questId));
            Assert.AreEqual(1, service.ReadLedger().SuccessfulGrants);
            Assert.Contains("content.hidden_passive", service.ReadLedger().ContentIds);

            Restart("b");
            manager.ActivateConfiguredQuests();
            Assert.AreEqual(0, service.ReadLedger().SuccessfulGrants);
            Assert.IsEmpty(service.ReadLedger().ContentIds);
            SaveQuest("b");

            Restart("a");
            Assert.AreEqual(0, manager.ActivateConfiguredQuests());
            WinEncounter();
            Assert.AreEqual(1, service.ReadLedger().SuccessfulGrants);
            Assert.AreEqual(1, manager.GetQuestCompletionCount(definition.questId));
        }

        [Test]
        public void RestartAfterRecipientCommitBeforeQuestReceiptDoesNotDuplicateDelivery()
        {
            Restart("a");
            service.InterruptAfterCommit = true;
            manager.ActivateConfiguredQuests();
            WinEncounter();
            SaveQuest("a");

            Assert.AreEqual(QuestState.Successful, manager.GetQuestState(definition.questId));
            Assert.IsEmpty(manager.GetSaveData().activeQuests.Single().grantedRewardIds);
            var grantedReceipt = service.ReadLedger().ReceiptIds.Single();
            Assert.AreEqual(1, service.ReadLedger().SuccessfulGrants);

            Restart("a");
            manager.ActivateConfiguredQuests();
            SaveQuest("a");

            Assert.AreEqual(QuestState.TheEnd, manager.GetQuestState(definition.questId));
            Assert.AreEqual(1, manager.GetQuestCompletionCount(definition.questId));
            Assert.AreEqual(1, service.ReadLedger().SuccessfulGrants);
            Assert.AreEqual(grantedReceipt, service.ReadLedger().ReceiptIds.Single());
            Restart("a");
            manager.ActivateConfiguredQuests();
            WinEncounter();
            Assert.AreEqual(1, service.ReadLedger().SuccessfulGrants);
        }

        [Test]
        public void SavedPerRunBoundaryAllowsOnlyOneNewGrantAfterRestart()
        {
            definition.lifecycle = QuestLifecycle.PerRun;
            Restart("a");
            manager.ActivateConfiguredQuests();
            WinEncounter();
            manager.ClearPerRunQuests();
            SaveQuest("a");

            Restart("a");
            Assert.AreEqual(1, manager.ActivateConfiguredQuests());
            Assert.AreEqual(1, manager.GetQuestCompletionCount(definition.questId));
            WinEncounter();
            WinEncounter();
            SaveQuest("a");

            Restart("a");
            Assert.AreEqual(0, manager.ActivateConfiguredQuests());
            WinEncounter();
            Assert.AreEqual(2, manager.GetQuestCompletionCount(definition.questId));
            Assert.AreEqual(2, service.ReadLedger().SuccessfulGrants);
            Assert.AreEqual(2, service.ReadLedger().ReceiptIds.Distinct().Count());
        }

        private void Restart(string slot)
        {
            if (manager) Object.DestroyImmediate(manager.gameObject);
            EventManager.Clear();
            QuestServiceRegistry.ResetForTests();
            service = new DiskRewardService(Path.Combine(directory, slot + "-recipient.json"));
            QuestServiceRegistry.SetRewardService(service);
            var go = new GameObject("IsolatedQuestDiskWorkflow");
            objects.Add(go);
            manager = go.AddComponent<QuestManager>();
            var path = Path.Combine(directory, slot + "-quest.json");
            manager.LoadSaveData(File.Exists(path)
                ? JsonUtility.FromJson<QuestSystemSaveData>(File.ReadAllText(path))
                : null);
            manager.RegisterConfig(definition);
        }

        private void SaveQuest(string slot) => WriteJson(Path.Combine(directory, slot + "-quest.json"), manager.GetSaveData());

        private void WinEncounter() => manager.ProcessConditionEvent("EncounterWon", new ConditionEventData("final_encounter"));

        private static void WriteJson(string path, object value)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(value));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        [Serializable]
        private sealed class ContentReward : QuestReward
        {
            public string ContentId;
            public override string RewardType => "TestContentUnlock";
            public override bool Grant() => throw new InvalidOperationException("A recipient adapter is required.");
            public override string GetPreviewText() => ContentId;
        }

        [Serializable]
        private sealed class RecipientLedger
        {
            public int SuccessfulGrants;
            public List<string> ReceiptIds = new List<string>();
            public List<string> ContentIds = new List<string>();
        }

        private sealed class DiskRewardService : IContextualQuestRewardService
        {
            private readonly string path;
            public bool InterruptAfterCommit;
            public DiskRewardService(string path) { this.path = path; }
            public RecipientLedger ReadLedger() => File.Exists(path)
                ? JsonUtility.FromJson<RecipientLedger>(File.ReadAllText(path)) : new RecipientLedger();

            public bool Grant(QuestReward reward, QuestConfigSO quest) =>
                throw new InvalidOperationException("A stable grant context is required.");

            public bool Grant(QuestReward reward, QuestConfigSO quest, QuestRewardGrantContext context)
            {
                var ledger = ReadLedger();
                if (ledger.ReceiptIds.Contains(context.ReceiptId)) return true;
                ledger.SuccessfulGrants++;
                ledger.ReceiptIds.Add(context.ReceiptId);
                var id = ((ContentReward)reward).ContentId;
                if (!ledger.ContentIds.Contains(id)) ledger.ContentIds.Add(id);
                WriteJson(path, ledger);
                if (InterruptAfterCommit)
                    throw new IOException("Simulated interruption after durable recipient commit.");
                return true;
            }
        }
    }
}
