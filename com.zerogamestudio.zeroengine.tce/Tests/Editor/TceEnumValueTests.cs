using System;
using NUnit.Framework;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceEnumValueTests
    {
        [Test]
        public void TceTargetMode_HasStableSerializedValues()
        {
            Assert.AreEqual(0, (int)TceTargetMode.FromTrigger);
            Assert.AreEqual(1, (int)TceTargetMode.Self);
            Assert.AreEqual(2, (int)TceTargetMode.Source);
            Assert.AreEqual(3, Enum.GetValues(typeof(TceTargetMode)).Length);
        }

        [Test]
        public void TceComparison_HasStableSerializedValues()
        {
            Assert.AreEqual(0, (int)TceComparison.GreaterThan);
            Assert.AreEqual(1, (int)TceComparison.LessThan);
            Assert.AreEqual(2, (int)TceComparison.GreaterThanOrEqualTo);
            Assert.AreEqual(3, (int)TceComparison.LessThanOrEqualTo);
            Assert.AreEqual(4, (int)TceComparison.EqualTo);
            Assert.AreEqual(5, Enum.GetValues(typeof(TceComparison)).Length);
        }

        [Test]
        public void TceComponentDocCategory_HasStableValues()
        {
            Assert.AreEqual(0, (int)TceComponentDocCategory.Trigger);
            Assert.AreEqual(1, (int)TceComponentDocCategory.Condition);
            Assert.AreEqual(2, (int)TceComponentDocCategory.Effect);
            Assert.AreEqual(3, Enum.GetValues(typeof(TceComponentDocCategory)).Length);
        }
    }
}
