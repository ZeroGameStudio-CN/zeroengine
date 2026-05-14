using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Quest.Tests.Editor
{
    public class QuestAcceptRequirementTests
    {
        private readonly List<ScriptableObject> assets = new();
        private readonly List<GameObject> objects = new();

        [SetUp]
        public void SetUp()
        {
            DestroyExistingQuestManagers();
            QuestServiceRegistry.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in assets)
                if (asset) Object.DestroyImmediate(asset);
            assets.Clear();

            foreach (var go in objects)
                if (go) Object.DestroyImmediate(go);
            objects.Clear();

            DestroyExistingQuestManagers();
        }

        [Test]
        public void AcceptQuest_AllowsQuestWithNoRequirements()
        {
            var manager = CreateManager(CreateQuest("quest_a"));

            Assert.That(manager.AcceptQuest("quest_a"), Is.True);
        }

        [Test]
        public void AcceptQuest_BlocksWhenPrerequisiteQuestIsNotTheEnd()
        {
            var prerequisite = CreateQuest("quest_a");
            var locked = CreateQuest("quest_b");
            locked.AcceptRequirements.Add(new QuestStateAcceptRequirement
            {
                questId = "quest_a",
                requiredState = QuestState.TheEnd
            });

            var manager = CreateManager(prerequisite, locked);

            Assert.That(manager.AcceptQuest("quest_b"), Is.False);
            Assert.That(manager.GetQuestState("quest_b"), Is.EqualTo(QuestState.Inactive));
        }

        [Test]
        public void AcceptQuest_AllowsWhenPrerequisiteQuestIsTheEnd()
        {
            var prerequisite = CreateQuest("quest_a");
            var locked = CreateQuest("quest_b");
            locked.AcceptRequirements.Add(new QuestStateAcceptRequirement
            {
                questId = "quest_a",
                requiredState = QuestState.TheEnd
            });

            var manager = CreateManager(prerequisite, locked);
            Assert.That(manager.AcceptQuest("quest_a"), Is.True);
            manager.ForceCompleteQuestForTests("quest_a");
            manager.SubmitQuest("quest_a");

            Assert.That(manager.AcceptQuest("quest_b"), Is.True);
        }

        [Test]
        public void AcceptQuest_RespectsInvertedQuestStateRequirement()
        {
            var prerequisite = CreateQuest("quest_a");
            var locked = CreateQuest("quest_b");
            locked.AcceptRequirements.Add(new QuestStateAcceptRequirement
            {
                questId = "quest_a",
                requiredState = QuestState.TheEnd,
                invert = true
            });

            var manager = CreateManager(prerequisite, locked);

            Assert.That(manager.AcceptQuest("quest_b"), Is.True);
        }

        [Test]
        public void CanAcceptQuest_ReturnsBlockReason()
        {
            var locked = CreateQuest("quest_b");
            locked.AcceptRequirements.Add(new QuestStateAcceptRequirement
            {
                questId = "quest_a",
                requiredState = QuestState.TheEnd
            });

            var manager = CreateManager(locked);

            Assert.That(manager.CanAcceptQuest("quest_b", out var reason), Is.False);
            StringAssert.Contains("quest_a", reason);
            StringAssert.Contains("TheEnd", reason);
        }

        private QuestConfigSO CreateQuest(string questId)
        {
            var quest = ScriptableObject.CreateInstance<QuestConfigSO>();
            quest.questId = questId;
            quest.questName = questId;
            assets.Add(quest);
            return quest;
        }

        private QuestManager CreateManager(params QuestConfigSO[] configs)
        {
            var go = new GameObject("QuestManagerTests");
            objects.Add(go);
            var manager = go.AddComponent<QuestManager>();

            foreach (var config in configs)
                manager.RegisterConfig(config);

            return manager;
        }

        private static void DestroyExistingQuestManagers()
        {
            foreach (var manager in Object.FindObjectsOfType<QuestManager>())
                Object.DestroyImmediate(manager.gameObject);
        }
    }
}
