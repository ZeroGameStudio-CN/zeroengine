using System;
using System.Collections.Generic;
using NUnit.Framework;
using ZeroEngine.Achievement;

namespace ZeroEngine.Narrative.Tests
{
    public sealed class AchievementTypeForwardingTests
    {
        [TestCase(typeof(AchievementManager))]
        [TestCase(typeof(AchievementSO))]
        [TestCase(typeof(AchievementSaveData))]
        [TestCase(typeof(AchievementProgress))]
        [TestCase(typeof(CounterCondition))]
        [TestCase(typeof(UnlockReward))]
        public void NarrativeAssemblyNameStillResolvesMovedType(Type expected)
        {
            Assert.That(Type.GetType(expected.FullName + ", ZeroEngine.Narrative"), Is.SameAs(expected));
        }

        [Test]
        public void LegacyClosedGenericSaveTypeResolvesMovedProgress()
        {
            var type = typeof(Dictionary<string, AchievementProgress>);
            var legacy = type.AssemblyQualifiedName.Replace(", ZeroEngine.Achievement,", ", ZeroEngine.Narrative,");
            Assert.That(Type.GetType(legacy), Is.SameAs(type));
        }
    }
}
