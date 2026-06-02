using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Timing.Tests
{
    public sealed class TimeControlLocatorTests
    {
        [TearDown]
        public void TearDown()
        {
            TimeControlLocator.ResetForTests();
        }

        [Test]
        public void RuntimeInitialization_ResetsStaticServiceOnSubsystemRegistration()
        {
            var method = typeof(TimeControlLocator).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(candidate => candidate.GetCustomAttributes<RuntimeInitializeOnLoadMethodAttribute>()
                    .Any(attribute => attribute.loadType == RuntimeInitializeLoadType.SubsystemRegistration));

            Assert.That(method, Is.Not.Null);

            TimeControlLocator.Service.Freeze(new object(), TimeDomainIds.Global);
            method.Invoke(null, null);

            Assert.That(TimeControlLocator.Service.GetScale(TimeDomainIds.Global), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(TimeControlLocator.Service.IsFrozen(TimeDomainIds.Global), Is.False);
        }
    }
}
