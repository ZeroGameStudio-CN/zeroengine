using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Quest;

namespace ZeroEngine.Narrative.Tests.Quest
{
    [TestFixture]
    public sealed class QuestSnapshotTests
    {
        [Test]
        public void Create_CollectCondition_IncludesObjectiveProgress()
        {
            var config = ScriptableObject.CreateInstance<QuestConfigSO>();
            try
            {
                config.questId = "quest.demo.longleji.herb_and_bandit";
                config.questName = "Herb And Bandit";
                config.submitNpcId = "npc.longleji.fuling";
                config.Conditions.Add(new CollectCondition
                {
                    ItemId = "wild_herb",
                    RequiredCount = 3,
                    Description = "Collect herbs"
                });

                var runtime = new QuestRuntimeData(config.questId)
                {
                    state = QuestState.Active
                };
                runtime.AddProgress("Collect_wild_herb", 2, 3);

                var snapshot = QuestSnapshotFactory.Create(runtime, config);

                Assert.AreEqual(config.questId, snapshot.QuestId);
                Assert.AreEqual(QuestState.Active, snapshot.State);
                Assert.AreEqual("npc.longleji.fuling", snapshot.SubmitTargetId);
                Assert.AreEqual(QuestLifecycle.Persistent, snapshot.TrackingPolicy);
                Assert.That(snapshot.Objectives, Has.Count.EqualTo(1));
                Assert.AreEqual("Collect_wild_herb", snapshot.Objectives[0].ObjectiveId);
                Assert.AreEqual("wild_herb", snapshot.Objectives[0].TargetId);
                Assert.AreEqual(2, snapshot.Objectives[0].Current);
                Assert.AreEqual(3, snapshot.Objectives[0].Target);
                Assert.IsFalse(snapshot.Objectives[0].Completed);
                Assert.AreEqual("Collect herbs", snapshot.Objectives[0].DisplayKey);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void QuestEvents_ExposeCanonicalDemoEventNames()
        {
            Assert.AreEqual("Quest.NpcTalked", QuestEvents.NpcTalked);
            Assert.AreEqual("Quest.ItemCollected", QuestEvents.ItemCollected);
            Assert.AreEqual("Quest.EnemyKilled", QuestEvents.EnemyKilled);
            Assert.AreEqual("Quest.BattleWon", QuestEvents.BattleWon);
            Assert.AreEqual("Quest.LocationReached", QuestEvents.LocationReached);
            Assert.AreEqual("Quest.InteractionCompleted", QuestEvents.InteractionCompleted);
        }
    }
}
