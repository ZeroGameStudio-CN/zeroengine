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

        [Test]
        public void TceValidationSeverity_HasStableValues()
        {
            Assert.AreEqual(0, (int)TceValidationSeverity.Info);
            Assert.AreEqual(1, (int)TceValidationSeverity.Warning);
            Assert.AreEqual(2, (int)TceValidationSeverity.Error);
            Assert.AreEqual(3, Enum.GetValues(typeof(TceValidationSeverity)).Length);
        }

        [Test]
        public void TceFlagLookupTarget_HasStableValues()
        {
            Assert.AreEqual(0, (int)TceFlagLookupTarget.Owner);
            Assert.AreEqual(1, (int)TceFlagLookupTarget.TriggerTarget);
            Assert.AreEqual(2, (int)TceFlagLookupTarget.Source);
            Assert.AreEqual(3, Enum.GetValues(typeof(TceFlagLookupTarget)).Length);
        }

        [Test]
        public void TceRandomLookupTarget_HasStableValues()
        {
            Assert.AreEqual(0, (int)TceRandomLookupTarget.Owner);
            Assert.AreEqual(1, (int)TceRandomLookupTarget.TriggerTarget);
            Assert.AreEqual(2, (int)TceRandomLookupTarget.InstallSource);
            Assert.AreEqual(3, (int)TceRandomLookupTarget.TriggerSource);
            Assert.AreEqual(4, Enum.GetValues(typeof(TceRandomLookupTarget)).Length);
        }
    }
}
