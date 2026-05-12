using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Dialog;
using Object = UnityEngine.Object;

namespace ZeroEngine.Quest.Tests.Editor
{
    public class QuestRuntimeServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            DestroyExistingQuestManagers();
            QuestServiceRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyExistingQuestManagers();
        }

        [Test]
        public void ServiceRegistry_DefaultsAreNullSafe()
        {
            Assert.NotNull(QuestServiceRegistry.ConfigSource);
            Assert.NotNull(QuestServiceRegistry.RewardService);
            Assert.NotNull(QuestServiceRegistry.LocalizationService);
            Assert.IsEmpty(QuestServiceRegistry.ConfigSource.LoadConfigs());
            Assert.False(QuestServiceRegistry.HasCustomConfigSource);
        }

        [Test]
        public void ServiceRegistry_AllowsProjectOverrides()
        {
            var configSource = new FakeConfigSource();
            var rewardService = new FakeRewardService();
            var localizationService = new FakeLocalizationService();

            QuestServiceRegistry.SetConfigSource(configSource);
            QuestServiceRegistry.SetRewardService(rewardService);
            QuestServiceRegistry.SetLocalizationService(localizationService);

            Assert.AreSame(configSource, QuestServiceRegistry.ConfigSource);
            Assert.AreSame(rewardService, QuestServiceRegistry.RewardService);
            Assert.AreSame(localizationService, QuestServiceRegistry.LocalizationService);
            Assert.True(QuestServiceRegistry.HasCustomConfigSource);
        }

        [Test]
        public void ServiceRegistry_NullConfigSourceRestoresDefaultFallbackMode()
        {
            QuestServiceRegistry.SetConfigSource(new FakeConfigSource());
            Assert.True(QuestServiceRegistry.HasCustomConfigSource);

            QuestServiceRegistry.SetConfigSource(null);

            Assert.NotNull(QuestServiceRegistry.ConfigSource);
            Assert.IsEmpty(QuestServiceRegistry.ConfigSource.LoadConfigs());
            Assert.False(QuestServiceRegistry.HasCustomConfigSource);
        }

        [Test]
        public void QuestManager_ReloadConfigsFromSource_AfterProjectSourceRegistration()
        {
            var quest = ScriptableObject.CreateInstance<QuestConfigSO>();
            quest.questId = "test_quest";

            var managerObject = new GameObject("QuestManager_Test");
            var manager = managerObject.AddComponent<QuestManager>();

            QuestServiceRegistry.SetConfigSource(new SingleConfigSource(quest));
            manager.ReloadConfigsFromSource();

            Assert.AreSame(quest, manager.GetConfig("test_quest"));

            Object.DestroyImmediate(managerObject);
            Object.DestroyImmediate(quest);
        }

        [Test]
        public void QuestManager_RegisterConfig_UsesTrimmedQuestIdKey()
        {
            var quest = ScriptableObject.CreateInstance<QuestConfigSO>();
            quest.questId = " test_quest ";

            var managerObject = new GameObject("QuestManager_TrimmedId_Test");
            var manager = managerObject.AddComponent<QuestManager>();

            manager.RegisterConfig(quest);

            Assert.AreSame(quest, manager.GetConfig("test_quest"));

            Object.DestroyImmediate(managerObject);
            Object.DestroyImmediate(quest);
        }

        [Test]
        public void DialogQuestCondition_EmptyQuestIdPasses()
        {
            var condition = new DialogQuestCondition();

            Assert.True(condition.Evaluate());
        }

        [Test]
        public void DialogQuestCondition_NonEmptyQuestIdFailsWithoutManager()
        {
            var condition = new DialogQuestCondition
            {
                questId = "missing_manager_quest",
                mode = DialogQuestConditionMode.Active
            };

            Assert.False(condition.Evaluate());
        }

        [Test]
        public void DialogQuestCondition_InvertReversesResult()
        {
            var condition = new DialogQuestCondition
            {
                questId = "missing_manager_quest",
                mode = DialogQuestConditionMode.Active,
                invert = true
            };

            Assert.True(condition.Evaluate());
        }

        [Test]
        public void DialogQuestCondition_CanSubmitMatchesSuccessfulState()
        {
            var quest = ScriptableObject.CreateInstance<QuestConfigSO>();
            quest.questId = "submit_ready_quest";
            quest.Conditions = new List<QuestCondition>
            {
                new CustomCondition
                {
                    EventType = "Quest.Test",
                    TargetId = "Target",
                    RequiredCount = 1
                }
            };

            var managerObject = new GameObject("QuestManager_DialogCondition_Test");
            var manager = managerObject.AddComponent<QuestManager>();
            manager.RegisterConfig(quest);
            Assert.True(manager.AcceptQuest("submit_ready_quest"));
            manager.ProcessConditionEvent("Quest.Test", new ConditionEventData("Target", 1));

            var condition = new DialogQuestCondition
            {
                questId = "submit_ready_quest",
                mode = DialogQuestConditionMode.CanSubmit
            };

            Assert.True(condition.Evaluate());

            Object.DestroyImmediate(managerObject);
            Object.DestroyImmediate(quest);
        }

        [Test]
        public void DialogQuestConditionGroup_OrSkipsNullConditionsAndRequiresARealMatch()
        {
            var group = new DialogQuestConditionGroup
            {
                CombineWith = LogicalOperator.Or,
                Conditions = new List<DialogQuestCondition>
                {
                    null,
                    new DialogQuestCondition
                    {
                        questId = "missing_manager_quest",
                        mode = DialogQuestConditionMode.Active
                    }
                }
            };

            Assert.False(group.Evaluate());

            group.Conditions.Add(new DialogQuestCondition());

            Assert.True(group.Evaluate());
        }

        private sealed class FakeConfigSource : IQuestConfigSource
        {
            public IReadOnlyList<QuestConfigSO> LoadConfigs() => new List<QuestConfigSO>();
        }

        private static void DestroyExistingQuestManagers()
        {
            foreach (var manager in Object.FindObjectsOfType<QuestManager>())
            {
                Object.DestroyImmediate(manager.gameObject);
            }
        }

        private sealed class SingleConfigSource : IQuestConfigSource
        {
            private readonly QuestConfigSO quest;

            public SingleConfigSource(QuestConfigSO quest)
            {
                this.quest = quest;
            }

            public IReadOnlyList<QuestConfigSO> LoadConfigs() => new[] { quest };
        }

        private sealed class FakeRewardService : IQuestRewardService
        {
            public bool Grant(QuestReward reward, QuestConfigSO quest) => true;
        }

        private sealed class FakeLocalizationService : IQuestLocalizationService
        {
            public string GetQuestTitle(QuestConfigSO quest) => quest == null ? string.Empty : quest.questName;
            public string GetQuestDescription(QuestConfigSO quest) => quest == null ? string.Empty : quest.description;
            public string GetConditionText(QuestConfigSO quest, QuestCondition condition) => condition == null ? string.Empty : condition.Description;
            public string GetRewardText(QuestConfigSO quest, QuestReward reward) => reward == null ? string.Empty : reward.GetPreviewText();
        }
    }
}
