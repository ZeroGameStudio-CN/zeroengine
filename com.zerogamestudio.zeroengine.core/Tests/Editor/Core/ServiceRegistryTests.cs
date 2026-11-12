using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Core.Tests
{
    [TestFixture]
    public sealed class ServiceRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            ServiceRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceRegistry.ClearForTests();
        }

        [Test]
        public void RegisterThenTryResolve_ReturnsRegisteredInstance()
        {
            var service = new TestService("primary");

            ServiceRegistry.Register<ITestService>(service);

            Assert.That(ServiceRegistry.TryResolve<ITestService>(out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(service));
        }

        [Test]
        public void ResolveOrNull_WhenRegistered_ReturnsRegisteredInstance()
        {
            var service = new TestService("primary");

            ServiceRegistry.Register<ITestService>(service);

            Assert.That(ServiceRegistry.ResolveOrNull<ITestService>(), Is.SameAs(service));
        }

        [Test]
        public void Register_NullService_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceRegistry.Register<ITestService>(null));
        }

        [Test]
        public void Unregister_WithSameInstance_RemovesRegistration()
        {
            var service = new TestService("primary");
            ServiceRegistry.Register<ITestService>(service);

            var removed = ServiceRegistry.Unregister<ITestService>(service);

            Assert.That(removed, Is.True);
            Assert.That(ServiceRegistry.TryResolve<ITestService>(out _), Is.False);
        }

        [Test]
        public void Unregister_WithDifferentInstance_KeepsExistingRegistration()
        {
            var original = new TestService("original");
            var other = new TestService("other");
            ServiceRegistry.Register<ITestService>(original);

            var removed = ServiceRegistry.Unregister<ITestService>(other);

            Assert.That(removed, Is.False);
            Assert.That(ServiceRegistry.TryResolve<ITestService>(out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(original));
        }

        [Test]
        public void Unregister_WithNoInstance_RemovesAnyRegistration()
        {
            var service = new TestService("primary");
            ServiceRegistry.Register<ITestService>(service);

            var removed = ServiceRegistry.Unregister<ITestService>();

            Assert.That(removed, Is.True);
            Assert.That(ServiceRegistry.TryResolve<ITestService>(out _), Is.False);
        }

        [Test]
        public void OverrideForTests_RestoresPreviousServiceWhenDisposed()
        {
            var original = new TestService("original");
            var replacement = new TestService("replacement");
            ServiceRegistry.Register<ITestService>(original);

            using (ServiceRegistry.OverrideForTests<ITestService>(replacement))
            {
                Assert.That(ServiceRegistry.TryResolve<ITestService>(out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(replacement));
            }

            Assert.That(ServiceRegistry.TryResolve<ITestService>(out var restored), Is.True);
            Assert.That(restored, Is.SameAs(original));
        }

        [Test]
        public void OverrideForTests_WhenNoPreviousService_RemovesOverrideWhenDisposed()
        {
            var replacement = new TestService("replacement");

            using (ServiceRegistry.OverrideForTests<ITestService>(replacement))
            {
                Assert.That(ServiceRegistry.TryResolve<ITestService>(out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(replacement));
            }

            Assert.That(ServiceRegistry.TryResolve<ITestService>(out _), Is.False);
        }

        [Test]
        public void OverrideForTests_NullService_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceRegistry.OverrideForTests<ITestService>(null));
        }

        [Test]
        public void SubsystemRegistrationReset_HasTheRequiredUnityLoadHook()
        {
            var reset = typeof(ServiceRegistry).GetMethod(
                "ResetForSubsystemRegistration",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(reset, Is.Not.Null);
            var attribute = reset.GetCustomAttributes<RuntimeInitializeOnLoadMethodAttribute>(false).Single();
            Assert.That(attribute.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        [Test]
        public void SubsystemRegistrationReset_ClearsStaleRegistrations()
        {
            var service = new TestService("stale");
            ServiceRegistry.Register<ITestService>(service);
            var reset = typeof(ServiceRegistry).GetMethod(
                "ResetForSubsystemRegistration",
                BindingFlags.NonPublic | BindingFlags.Static);

            reset.Invoke(null, null);

            Assert.That(ServiceRegistry.ResolveOrNull<ITestService>(), Is.Null);
        }

        private interface ITestService
        {
            string Name { get; }
        }

        private sealed class TestService : ITestService
        {
            public TestService(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }
    }
}
