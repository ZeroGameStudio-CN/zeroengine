using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Pool;

namespace ZeroEngine.Core.Tests
{
    public sealed class PersistentSingletonLifecycleTests
    {
        private static readonly FieldInfo Quitting = typeof(Singleton<>).Assembly
            .GetType("ZeroEngine.Core.SingletonRuntimeState")
            .GetField("ApplicationIsQuitting", BindingFlags.Static | BindingFlags.NonPublic);

        private bool previousQuitting;
        private GameObject owner;

        [SetUp]
        public void SetUp()
        {
            previousQuitting = (bool)Quitting.GetValue(null);
            Quitting.SetValue(null, false);
            owner = new GameObject("PersistentSingletonLifecycleTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (owner) Object.DestroyImmediate(owner);
            foreach (var probe in FindProbes())
                Object.DestroyImmediate(probe.gameObject);
            Quitting.SetValue(null, previousQuitting);
        }

        [UnityTest]
        public IEnumerator DestroyedInstanceAfterQuit_IsNotRecreatedByLateConsumers()
        {
            Assert.That(Application.isPlaying, Is.True);
            var probe = owner.AddComponent<PersistentSingletonProbe>();
            Assert.That(PersistentSingletonProbe.Instance, Is.SameAs(probe));

            probe.NotifyApplicationQuit();
            Object.Destroy(owner);
            yield return null;

            Assert.That(PersistentSingletonProbe.Instance, Is.Null);
            Assert.That(FindProbes(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator DestroyingDuplicate_DoesNotClearTheLiveOwner()
        {
            Assert.That(Application.isPlaying, Is.True);
            var original = owner.AddComponent<PersistentSingletonProbe>();
            var duplicateOwner = new GameObject("PersistentSingletonDuplicate");
            var duplicate = duplicateOwner.AddComponent<PersistentSingletonProbe>();
            try
            {
                yield return null;
                Assert.That(duplicate == null, Is.True, "The duplicate must be destroyed by its normal Awake lifecycle.");
                Assert.That(PersistentSingletonProbe.Instance, Is.SameAs(original));
            }
            finally
            {
                if (duplicateOwner) Object.DestroyImmediate(duplicateOwner);
            }
        }

        [UnityTest]
        public IEnumerator PoolManagerCleanup_ClearsItsSingletonOwner()
        {
            var pool = owner.AddComponent<PoolManager>();
            Assert.That(PoolManager.Instance, Is.SameAs(pool));
            Object.Destroy(owner);
            yield return null;

            var instance = typeof(PersistentSingleton<PoolManager>).GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(ReferenceEquals(instance.GetValue(null), null), Is.True,
                "Pool cleanup must also release the base singleton's managed reference.");
        }

        private static PersistentSingletonProbe[] FindProbes()
        {
#if UNITY_6000_6_OR_NEWER
            return Object.FindObjectsByType<PersistentSingletonProbe>();
#else
            return Object.FindObjectsByType<PersistentSingletonProbe>(FindObjectsSortMode.None);
#endif
        }
    }

    public sealed class PersistentSingletonProbe : PersistentSingleton<PersistentSingletonProbe>
    {
        public void NotifyApplicationQuit() => OnApplicationQuit();
    }
}
