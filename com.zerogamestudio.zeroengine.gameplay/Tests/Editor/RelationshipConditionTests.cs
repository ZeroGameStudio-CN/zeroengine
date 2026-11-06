using NUnit.Framework;
using UnityEngine;
using ZeroEngine.Interaction;

namespace ZeroEngine.Gameplay.Tests
{
    [TestFixture, Category("Unit")]
    public sealed class RelationshipConditionTests
    {
        [Test]
        public void MissingQuery_RefusesInsteadOfTreatingTheConditionAsSatisfied()
        {
            var condition = new RelationshipCondition { NpcId = "npc.teacher", MinLevel = 0 };
            Assert.That(condition.IsSatisfied(new InteractionContext(null, Vector3.zero, 0), null), Is.False);
        }
        [Test]
        public void UnknownNpc_RefusesEvenWhenTheThresholdIsZero()
        {
            var query = new Query { Known = false };
            Assert.That(Check(query, 0), Is.False);
        }
        [TestCase(0, 1, false)]
        [TestCase(1, 1, true)]
        [TestCase(3, 2, true)]
        [TestCase(-1, 0, false)]
        public void ResolvedLevel_UsesTheConfiguredThreshold(int level, int minimum, bool expected)
        {
            Assert.That(Check(new Query { Level = level }, minimum), Is.EqualTo(expected));
        }
        [TestCase("")]
        [TestCase(" ")]
        public void MissingNpcIdentity_RefusesWithoutQueryingAnotherActor(string id)
        {
            var query = new Query { Level = 3 };
            var condition = new RelationshipCondition { NpcId = id, MinLevel = 0 };
            Assert.That(condition.IsSatisfied(new InteractionContext(null, Vector3.zero, 0, query), null), Is.False);
            Assert.That(query.Calls, Is.Zero);
        }
        [Test]
        public void SeparateContexts_UseTheirOwnActorsFacts()
        {
            var first = new Query { Level = 0 }; var second = new Query { Level = 3 };
            Assert.That(Check(first, 2), Is.False); Assert.That(Check(second, 2), Is.True);
            Assert.That(first.LastId, Is.EqualTo("npc.teacher")); Assert.That(second.LastId, Is.EqualTo("npc.teacher"));
        }
        private static bool Check(Query query, int minimum) =>
            new RelationshipCondition { NpcId = "npc.teacher", MinLevel = minimum }
                .IsSatisfied(new InteractionContext(null, Vector3.zero, 0, query), null);
        private sealed class Query : IInteractionRelationshipQuery
        {
            public bool Known = true;
            public int Level, Calls;
            public string LastId;
            public bool TryGetLevel(string id, out int level) { Calls++; LastId = id; level = Level; return Known; }
        }
    }
}
