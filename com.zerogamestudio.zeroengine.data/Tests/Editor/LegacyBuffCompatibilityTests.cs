using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Data.Tests
{
    // Existing facade contracts remain valid when it delegates storage and clocks to the kernel.
    public class LegacyBuffCompatibilityTests
    {
        private ZeroEngine.BuffSystem.BuffData data;
        private ZeroEngine.BuffSystem.BuffHandler handler;

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<ZeroEngine.BuffSystem.BuffData>();
            data.Duration = 1; data.MaxStacks = 3; data.TickInterval = 1;
            handler = new ZeroEngine.BuffSystem.BuffHandler(data, null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(data);

        [Test]
        public void CapRefreshResetAndForcedExpiryKeepTheirPublicBehavior()
        {
            handler.AddStacks(9); Assert.That(handler.CurrentStacks, Is.EqualTo(3));
            handler.Tick(.5f); Assert.That(handler.RemainingTime, Is.EqualTo(.5f));
            handler.AddStacks(1); Assert.That(handler.RemainingTime, Is.EqualTo(1));
            handler.ResetStacks(); Assert.That(handler.CurrentStacks, Is.Zero); Assert.That(handler.IsExpired, Is.False);
            handler.AddStacks(1); handler.ForceExpire();
            Assert.That(handler.IsExpired, Is.True); Assert.That(handler.CurrentStacks, Is.Zero);
            handler.AddStacks(1); Assert.That(handler.CurrentStacks, Is.Zero);
        }

        [Test]
        public void ExpiryStillPrecedesTheFinalTickInTheLegacyFacade()
        {
            var order = new List<string>();
            handler.OnExpired += _ => order.Add("expired");
            handler.OnTick += _ => order.Add("tick");
            handler.AddStacks(1); handler.Tick(1);
            Assert.That(order, Is.EqualTo(new[] { "expired", "tick" }));
            handler.Tick(10); Assert.That(order.Count, Is.EqualTo(2));
        }

        [Test]
        public void OneStackExpiryRefreshesEvenWhenManualRemovalDoesNot()
        {
            data.ExpireMode = ZeroEngine.BuffSystem.BuffExpireMode.RemoveOneStack;
            data.RefreshOnRemoveStack = false;
            handler.AddStacks(3); handler.Tick(1);
            Assert.That(handler.CurrentStacks, Is.EqualTo(2));
            Assert.That(handler.RemainingTime, Is.EqualTo(1));
            handler.Tick(.25f); handler.RemoveStacks(1);
            Assert.That(handler.RemainingTime, Is.EqualTo(.75f));
        }

        [Test]
        public void SignedCountsKeepLegacyFacadeArithmetic()
        {
            handler.AddStacks(-1); Assert.That(handler.CurrentStacks, Is.EqualTo(-1));
            handler.RemoveStacks(-3); Assert.That(handler.CurrentStacks, Is.EqualTo(2));
            Assert.That(handler.IsExpired, Is.False);
        }
    }
}
