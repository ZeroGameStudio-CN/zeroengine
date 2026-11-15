using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Pool.Tests
{
    public sealed class PoolSpawnLeasePlayModeTests
    {
        private PoolManager _pool;
        private GameObject _prefab;
        private Action<CallbackProbe> _previousSpawn;
        private Action<CallbackProbe> _previousDespawn;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.isPlaying, Is.True);
            Assert.That(Object.FindObjectsOfType<PoolManager>().Length, Is.Zero, "Preserve pre-existing pool owners.");
            _previousSpawn = CallbackProbe.SpawnCallback;
            _previousDespawn = CallbackProbe.DespawnCallback;
            CallbackProbe.SpawnCallback = CallbackProbe.DespawnCallback = null;
            _pool = new GameObject("Owned core lease pool").AddComponent<PoolManager>();
            _prefab = new GameObject("Owned core lease prefab");
            _prefab.SetActive(false);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CallbackProbe.SpawnCallback = _previousSpawn;
            CallbackProbe.DespawnCallback = _previousDespawn;
            if (_pool != null) Object.DestroyImmediate(_pool.gameObject);
            if (_prefab != null) Object.DestroyImmediate(_prefab);
            yield return null;
        }

        [Test]
        public void OwnedSpawn_InactiveSetupAndCopiedReturnAreSafe()
        {
            var lease = _pool.SpawnLease(_prefab, Vector3.one, Quaternion.identity, autoActive: false);
            var copy = lease;
            var instance = lease.Instance;
            Assert.That(lease.IsCurrent, Is.True);
            Assert.That(instance.activeSelf, Is.False);
            instance.SetActive(true);
            Assert.That(lease.TryDespawn(), Is.True);
            Assert.That(copy.TryDespawn(), Is.False);
            copy.Dispose();
            Assert.That(lease.IsCurrent, Is.False);
            Assert.That(lease.Instance, Is.Null);
            Assert.That(instance.activeSelf, Is.False);
        }

        [Test]
        public void RawReuse_InvalidatesThePreviousLease()
        {
            var old = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            var instance = old.Instance;
            _pool.Despawn(instance);
            var current = _pool.Spawn(_prefab, Vector3.right * 5f, Quaternion.identity);
            Assert.That(current, Is.SameAs(instance));
            Assert.That(old.IsCurrent, Is.False);
            Assert.That(old.TryDespawn(), Is.False);
            old.Dispose();
            Assert.That(current.activeSelf, Is.True);
            Assert.That(current.transform.position, Is.EqualTo(Vector3.right * 5f));
            _pool.Despawn(current);
        }

        [Test]
        public void LeaseReuse_GetsADistinctReturnRight()
        {
            var old = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            var instance = old.Instance;
            Assert.That(old.TryDespawn(), Is.True);
            var current = _pool.SpawnLease(_prefab, Vector3.one, Quaternion.identity);
            Assert.That(current.Instance, Is.SameAs(instance));
            Assert.That(current.Equals(old), Is.False);
            old.Dispose();
            Assert.That(current.IsCurrent, Is.True);
            Assert.That(current.TryDespawn(), Is.True);
        }

        [Test]
        public void DefaultLease_IsAnIdempotentNoOp()
        {
            var lease = default(PoolSpawnLease);
            Assert.That(lease.IsCurrent, Is.False);
            Assert.That(lease.Instance, Is.Null);
            Assert.That(lease.TryDespawn(), Is.False);
            lease.Dispose();
        }

        [Test]
        public void DestroyedObject_StillRetiresItsAccountingOnce()
        {
            var lease = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(lease.Instance);
            Assert.That(lease.IsCurrent, Is.False);
            Assert.That(lease.TryDespawn(), Is.True);
            Assert.That(lease.TryDespawn(), Is.False);
        }

        [Test]
        public void ClearPool_RevokesOldRightsWithoutTouchingNewSpawns()
        {
            var old = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            _pool.ClearAllPools();
            Assert.That(old.IsCurrent, Is.False);
            var current = _pool.SpawnLease(_prefab, Vector3.one, Quaternion.identity);
            old.Dispose();
            Assert.That(current.IsCurrent, Is.True);
            Assert.That(current.Instance.activeSelf, Is.True);
            current.Dispose();
        }

        [Test]
        public void OnDespawn_RespawnCannotBeDisabledByThePreviousReturn()
        {
            _prefab.AddComponent<CallbackProbe>();
            var old = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            var instance = old.Instance;
            var current = default(PoolSpawnLease);
            CallbackProbe.DespawnCallback = _ => current = _pool.SpawnLease(_prefab, Vector3.right, Quaternion.identity);
            Assert.That(old.TryDespawn(), Is.True);
            CallbackProbe.DespawnCallback = null;
            Assert.That(current.Instance, Is.SameAs(instance));
            Assert.That(current.Instance.activeSelf, Is.True);
            Assert.That(old.TryDespawn(), Is.False);
            current.Dispose();
        }

        [Test]
        public void OnSpawn_ReentryDoesNotLendTheNewerGenerationToTheOldCaller()
        {
            _prefab.AddComponent<CallbackProbe>();
            var current = default(PoolSpawnLease);
            bool enter = true;
            CallbackProbe.SpawnCallback = probe =>
            {
                if (!enter) return;
                enter = false;
                _pool.Despawn(probe.gameObject);
                current = _pool.SpawnLease(_prefab, Vector3.up, Quaternion.identity);
            };
            var old = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity, autoActive: false);
            CallbackProbe.SpawnCallback = null;
            Assert.That(old.IsCurrent, Is.False);
            Assert.That(old.Instance, Is.Null);
            Assert.That(current.IsCurrent, Is.True);
            Assert.That(current.Instance.activeSelf, Is.True, "The old autoActive=false cannot hide the reentrant spawn.");
            old.Dispose();
            current.Dispose();
        }

        [Test]
        public void SpawnCallbackFailure_RollsBackItsOwnSpawn()
        {
            _prefab.AddComponent<CallbackProbe>();
            GameObject failed = null;
            CallbackProbe.SpawnCallback = probe => { failed = probe.gameObject; throw new InvalidOperationException("owned-spawn-failure"); };
            Assert.Throws<InvalidOperationException>(() => _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity));
            CallbackProbe.SpawnCallback = null;
            var current = _pool.SpawnLease(_prefab, Vector3.one, Quaternion.identity);
            Assert.That(current.Instance, Is.SameAs(failed));
            Assert.That(current.IsCurrent, Is.True);
            current.Dispose();
        }

        [Test]
        public void DespawnCallbackFailure_StillEndsThatReturnRight()
        {
            _prefab.AddComponent<CallbackProbe>();
            var lease = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            var instance = lease.Instance;
            CallbackProbe.DespawnCallback = _ => throw new InvalidOperationException("owned-return-failure");
            Assert.Throws<InvalidOperationException>(() => lease.TryDespawn());
            CallbackProbe.DespawnCallback = null;
            Assert.That(lease.IsCurrent, Is.False);
            Assert.That(lease.TryDespawn(), Is.False);
            Assert.That(instance.activeSelf, Is.False);
            var current = _pool.SpawnLease(_prefab, Vector3.one, Quaternion.identity);
            Assert.That(current.Instance, Is.SameAs(instance));
            current.Dispose();
        }

        [Test]
        public void DestroyedManager_DisposalCannotCreateAReplacement()
        {
            var lease = _pool.SpawnLease(_prefab, Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(_pool.gameObject);
            Assert.That(lease.IsCurrent, Is.False);
            Assert.That(lease.TryDespawn(), Is.False);
            lease.Dispose();
            Assert.That(Object.FindObjectsOfType<PoolManager>().Length, Is.Zero);
        }

        public sealed class CallbackProbe : MonoBehaviour, IPoolable
        {
            public static Action<CallbackProbe> SpawnCallback;
            public static Action<CallbackProbe> DespawnCallback;
            public void OnSpawn() => SpawnCallback?.Invoke(this);
            public void OnDespawn() => DespawnCallback?.Invoke(this);
        }
    }
}
