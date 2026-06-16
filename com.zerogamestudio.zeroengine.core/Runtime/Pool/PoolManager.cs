using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITASK_ENABLED
using System.Threading;
using Cysharp.Threading.Tasks;
#endif

#if ADDRESSABLES_ENABLED
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#endif

namespace ZeroEngine.Pool
{
    /// <summary>
    /// 对象池类型分类
    /// </summary>
    public enum PoolType
    {
        None,
        UI,
        VFX,
        Audio,
        Characters,
        Projectiles
    }

    /// <summary>
    /// 通用对象池管理器
    /// 支持 Prefab 直接生成和 Addressables 异步加载（需启用 ADDRESSABLES_ENABLED）
    /// </summary>
    public class PoolManager : Core.PersistentSingleton<PoolManager>
    {
        #region SmartPool Class

        [Serializable]
        private class SmartPool
        {
            private readonly Queue<GameObject> _queue = new();

            private readonly HashSet<GameObject> _activeObjects = new();

            private readonly GameObject _prefab;
            private readonly Transform _parent;
            private float _lastDespawnTime;

            public int PrefabId { get; }
            public int CountAll => _queue.Count + _activeObjects.Count;
            public int CountInactive => _queue.Count;
            public int CountActive => _activeObjects.Count;

            public int MaxCapacity { get; set; } = 1000;
            public float ShrinkInterval { get; set; } = 60f;
            public int MinSize { get; set; }

            public SmartPool(GameObject prefab, Transform parent, int minSize = 0)
            {
                _prefab = prefab;
                _parent = parent;
                PrefabId = prefab.GetInstanceID();
                MinSize = minSize;
                _lastDespawnTime = Time.time;
            }

            public GameObject Spawn(Vector3 position, Quaternion rotation)
            {
                GameObject obj;
                if (_queue.Count > 0)
                {
                    obj = _queue.Dequeue();
                    if (obj == null) return Spawn(position, rotation);
                }
                else
                {
                    if (CountAll >= MaxCapacity) return null;
                    obj = UnityEngine.Object.Instantiate(_prefab, position, rotation, _parent);
                    obj.name = _prefab.name;
                }

                obj.transform.SetParent(null);
                obj.transform.position = position;
                obj.transform.rotation = rotation;
                _activeObjects.Add(obj);
                return obj;
            }

            public bool Despawn(GameObject obj)
            {
                if (!_activeObjects.Remove(obj)) return false;

                obj.transform.SetParent(_parent);
                _queue.Enqueue(obj);
                _lastDespawnTime = Time.time;
                return true;
            }

            public void Shrink()
            {
                if (Time.time - _lastDespawnTime < ShrinkInterval || _queue.Count <= MinSize) return;

                int toShrink = _queue.Count - MinSize;
                for (int i = 0; i < toShrink; i++)
                {
                    UnityEngine.Object.Destroy(_queue.Dequeue());
                }
            }

            public void Clear()
            {
                foreach (var obj in _queue)
                    if (obj)
                        UnityEngine.Object.Destroy(obj);
                foreach (var obj in _activeObjects)
                    if (obj)
                        UnityEngine.Object.Destroy(obj);
                _queue.Clear();
                _activeObjects.Clear();
            }
        }

        #endregion

        private readonly Dictionary<int, SmartPool> _poolsById = new();

#if ADDRESSABLES_ENABLED
        private readonly Dictionary<string, SmartPool> _poolsByAddressableKey = new();
#endif

        private readonly Dictionary<GameObject, SmartPool> _globalActiveLookup = new();
        private readonly Dictionary<PoolType, Transform> _poolParents = new();

#if ADDRESSABLES_ENABLED
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _addressableHandles = new();
#endif

        private Transform _poolsRoot;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _poolsRoot = new GameObject("___PoolManager___").transform;
            _poolsRoot.SetParent(transform);

            foreach (PoolType poolType in Enum.GetValues(typeof(PoolType)))
            {
                if (poolType == PoolType.None) continue;
                var poolParentGO = new GameObject($"{poolType} Pool");
                poolParentGO.transform.SetParent(_poolsRoot);
                _poolParents.Add(poolType, poolParentGO.transform);
            }

#if UNITASK_ENABLED
            ShrinkPoolsRoutine(this.GetCancellationTokenOnDestroy()).Forget();
#else
            StartCoroutine(ShrinkPoolsCoroutine());
#endif
        }

        #region Public API - Sync

        /// <summary>
        /// 从池中生成对象
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, PoolType poolType = PoolType.None, bool autoActive = true)
        {
            if (prefab == null) return null;
            var pool = GetOrCreatePool(prefab, poolType);
            return SpawnInternal(pool, position, rotation, autoActive);
        }

        /// <summary>
        /// 从池中生成对象（泛型版本）
        /// </summary>
        public T Spawn<T>(T prefabComponent, Vector3 position, Quaternion rotation, PoolType poolType = PoolType.None, bool autoActive = true) where T : Component
        {
            var obj = Spawn(prefabComponent.gameObject, position, rotation, poolType, autoActive);
            return obj != null ? obj.GetComponent<T>() : null;
        }

        /// <summary>
        /// 归还对象到池中
        /// </summary>
        public void Despawn(GameObject obj)
        {
            if (!Application.isPlaying) return;
            if (obj == null) return;

            if (_globalActiveLookup.TryGetValue(obj, out var pool))
            {
                if (pool.Despawn(obj))
                {
                    _globalActiveLookup.Remove(obj);
                    if (obj.TryGetComponent<IPoolable>(out var poolable)) poolable.OnDespawn();
                    obj.SetActive(false);
                }
            }
            else
            {
                Debug.LogWarning($"[PoolManager] Despawn failed. Object {obj.name} not found in active lookup. Destroying it.");
                Destroy(obj);
            }
        }

        #endregion

        #region Public API - Async (Requires UNITASK_ENABLED)

#if UNITASK_ENABLED
        /// <summary>
        /// 预加载对象到池中（分帧）
        /// </summary>
        public async UniTask PreloadAsync(GameObject prefab, int count, PoolType poolType = PoolType.None, int minSize = 0)
        {
            var pool = GetOrCreatePool(prefab, poolType);
            pool.MinSize = Math.Max(pool.MinSize, minSize);
            for (int i = 0; i < count; i++)
            {
                var obj = SpawnInternal(pool, Vector3.zero, Quaternion.identity, false);
                if (obj != null) Despawn(obj);
                await UniTask.Yield();
            }
        }
#endif

        #endregion

        #region Public API - Addressables (Requires ADDRESSABLES_ENABLED + UNITASK_ENABLED)

#if ADDRESSABLES_ENABLED && UNITASK_ENABLED
        /// <summary>
        /// 从 Addressables 异步生成对象
        /// </summary>
        public async UniTask<GameObject> SpawnAsync(AssetReferenceGameObject assetReference, Vector3 position, Quaternion rotation, PoolType poolType = PoolType.None, bool autoActive = true)
        {
            if (!assetReference.RuntimeKeyIsValid()) return null;
            var pool = await GetOrCreatePoolAsync(assetReference, poolType);
            return SpawnInternal(pool, position, rotation, autoActive);
        }

        /// <summary>
        /// 从 Addressables 异步生成对象（泛型版本）
        /// </summary>
        public async UniTask<T> SpawnAsync<T>(AssetReferenceGameObject assetReference, Vector3 position, Quaternion rotation, PoolType poolType = PoolType.None, bool autoActive = true) where T : Component
        {
            var obj = await SpawnAsync(assetReference, position, rotation, poolType, autoActive);
            return obj != null ? obj.GetComponent<T>() : null;
        }

        /// <summary>
        /// 预加载 Addressable 对象到池中
        /// </summary>
        public async UniTask PreloadAsync(AssetReferenceGameObject assetReference, int count, PoolType poolType = PoolType.None, int minSize = 0)
        {
            var pool = await GetOrCreatePoolAsync(assetReference, poolType);
            if (pool == null) return;
            pool.MinSize = Math.Max(pool.MinSize, minSize);
            for (int i = 0; i < count; i++)
            {
                var obj = SpawnInternal(pool, Vector3.zero, Quaternion.identity, false);
                if (obj != null) Despawn(obj);
                await UniTask.Yield();
            }
        }
#endif

        #endregion

        #region Internal Logic

        private GameObject SpawnInternal(SmartPool pool, Vector3 position, Quaternion rotation, bool autoActive)
        {
            if (pool == null) return null;
            var obj = pool.Spawn(position, rotation);
            if (obj != null)
            {
                _globalActiveLookup[obj] = pool;
                if (obj.TryGetComponent<IPoolable>(out var poolable)) poolable.OnSpawn();
                obj.SetActive(autoActive);
            }
            return obj;
        }

        private SmartPool GetOrCreatePool(GameObject prefab, PoolType poolType)
        {
            int id = prefab.GetInstanceID();
            if (!_poolsById.TryGetValue(id, out var pool))
            {
                pool = new SmartPool(prefab, _poolParents.GetValueOrDefault(poolType, _poolsRoot));
                _poolsById[id] = pool;
            }
            return pool;
        }

#if ADDRESSABLES_ENABLED && UNITASK_ENABLED
        private async UniTask<SmartPool> GetOrCreatePoolAsync(AssetReferenceGameObject assetReference, PoolType poolType)
        {
            string key = assetReference.RuntimeKey.ToString();
            if (!_poolsByAddressableKey.TryGetValue(key, out var pool))
            {
                var prefab = await LoadAddressablePrefab(assetReference);
                if (prefab == null) return null;
                pool = new SmartPool(prefab, _poolParents.GetValueOrDefault(poolType, _poolsRoot));
                _poolsByAddressableKey[key] = pool;
            }
            return pool;
        }

        private async UniTask<GameObject> LoadAddressablePrefab(AssetReferenceGameObject assetReference)
        {
            string key = assetReference.RuntimeKey.ToString();
            if (_addressableHandles.TryGetValue(key, out var handle))
            {
                await handle.ToUniTask();
                return handle.Result;
            }

            handle = assetReference.LoadAssetAsync<GameObject>();
            _addressableHandles[key] = handle;
            await handle.ToUniTask();

            if (handle.Status == AsyncOperationStatus.Succeeded) return handle.Result;

            Debug.LogError($"[PoolManager] Failed to load Addressable: {key}");
            _addressableHandles.Remove(key);
            Addressables.Release(handle);
            return null;
        }
#endif

#if UNITASK_ENABLED
        private async UniTask ShrinkPoolsRoutine(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(10), ignoreTimeScale: true, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                foreach (var pool in _poolsById.Values) pool.Shrink();
#if ADDRESSABLES_ENABLED
                foreach (var pool in _poolsByAddressableKey.Values) pool.Shrink();
#endif
            }
        }
#else
        private System.Collections.IEnumerator ShrinkPoolsCoroutine()
        {
            var wait = new WaitForSecondsRealtime(10f);
            while (true)
            {
                yield return wait;
                foreach (var pool in _poolsById.Values) pool.Shrink();
            }
        }
#endif

        private void OnDestroy()
        {
            ClearAllPools();
        }

        /// <summary>
        /// 清除所有池
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _poolsById.Values) pool.Clear();
            _poolsById.Clear();
#if ADDRESSABLES_ENABLED
            foreach (var pool in _poolsByAddressableKey.Values) pool.Clear();
            _poolsByAddressableKey.Clear();
#endif
            _globalActiveLookup.Clear();

#if ADDRESSABLES_ENABLED
            foreach (var handle in _addressableHandles.Values)
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
            _addressableHandles.Clear();
#endif
        }

        #endregion
    }

    /// <summary>
    /// GameObject 扩展方法
    /// </summary>
    public static class PoolExtensions
    {
        /// <summary>
        /// 归还对象到池中
        /// </summary>
        public static void Despawn(this GameObject obj)
        {
            if (PoolManager.Instance != null)
                PoolManager.Instance.Despawn(obj);
        }
    }
}
