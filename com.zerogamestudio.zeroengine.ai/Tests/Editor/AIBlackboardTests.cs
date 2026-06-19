using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZeroEngine.AI;

namespace ZeroEngine.AI.Editor.Tests
{
    public sealed class AIBlackboardTests
    {
        [Test]
        public void SetGetAndTypedHelpersReturnStoredValues()
        {
            var blackboard = new AIBlackboard();

            blackboard.Set("health", 12);
            blackboard.Set("position", new Vector3(1f, 2f, 3f));

            Assert.AreEqual(12, blackboard.GetInt("health"));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), blackboard.GetVector3("position"));
            Assert.IsTrue(blackboard.Contains("health"));
            Assert.AreEqual(2, blackboard.Count);
        }

        [Test]
        public void GetConvertsCompatiblePrimitiveValuesButTryGetRequiresType()
        {
            var blackboard = new AIBlackboard();
            blackboard.Set("count", "42");

            Assert.AreEqual(42, blackboard.GetInt("count"));
            Assert.IsFalse(blackboard.TryGet<int>("count", out _));
        }

        [Test]
        public void IncrementUpdatesNumericValues()
        {
            var blackboard = new AIBlackboard();

            blackboard.Increment("score", 3);
            blackboard.Increment("score", 4);
            blackboard.IncrementFloat("threat", 1.5f);

            Assert.AreEqual(7, blackboard.GetInt("score"));
            Assert.AreEqual(1.5f, blackboard.GetFloat("threat"));
        }

        [Test]
        public void RemoveRaisesEventAndClearsKey()
        {
            var removedKeys = new List<string>();
            var blackboard = new AIBlackboard();
            blackboard.Set("target", "enemy");
            blackboard.OnValueRemoved += removedKeys.Add;

            Assert.IsTrue(blackboard.Remove("target"));

            Assert.IsFalse(blackboard.Contains("target"));
            Assert.AreEqual("target", removedKeys.Single());
        }
    }
}
