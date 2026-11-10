using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Core;

namespace ZeroEngine.Audio.Tests
{
    public sealed class AudioEventServiceLifecycleTests
    {
        private readonly List<GameObject> owners = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assert.That(Application.isPlaying, Is.True);
            ServiceRegistry.ClearForTests();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var owner in owners) if (owner) Object.Destroy(owner);
            owners.Clear();
            yield return null;
            ServiceRegistry.ClearForTests();
        }

        [UnityTest]
        public IEnumerator EventService_EnabledLifecycleRegistersAndUnregistersApplicationOwner()
        {
            var owner = new GameObject("AudioEventServiceOwner");
            owners.Add(owner);
            var service = owner.AddComponent<UnityAudioEventService>();
            yield return null;
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(service));
            service.enabled = false;
            yield return null;
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.Null);
            service.enabled = true;
            yield return null;
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(service));
        }

        [UnityTest]
        public IEnumerator EventService_SecondEnabledOwnerFailsClosed()
        {
            var firstOwner = new GameObject("FirstAudioEventServiceOwner");
            var secondOwner = new GameObject("SecondAudioEventServiceOwner");
            owners.Add(firstOwner);
            owners.Add(secondOwner);
            var first = firstOwner.AddComponent<UnityAudioEventService>();
            LogAssert.Expect(LogType.Error,
                "[UnityAudioEventService] Another IAudioEventService owner is already registered. " +
                "Only one enabled application owner is allowed.");
            var second = secondOwner.AddComponent<UnityAudioEventService>();
            yield return null;
            Assert.That(second.enabled, Is.False);
            Assert.That(ServiceRegistry.ResolveOrNull<IAudioEventService>(), Is.SameAs(first));
        }
    }
}
