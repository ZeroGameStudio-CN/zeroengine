using System.Collections.Generic;
using UnityEngine;

#if UNITY_6000_6_OR_NEWER
using SpawnObjectId = UnityEngine.EntityId;
#else
using SpawnObjectId = System.Int32;
#endif

namespace ZeroEngine.Spawner
{
    /// <summary>
    /// 生成管理器 - 管理所有生成器和对象池
    /// </summary>
    public class SpawnManager : Core.MonoSingleton<SpawnManager>
    {
        [Header("Pool Settings")]
        [SerializeField] private int _defaultPoolSize = 10;
        [SerializeField] private int _maxPoolSize = 100;
        [SerializeField] private bool _enablePooling = true;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        // 对象池
        private static SpawnObjectId GetRuntimeObjectId(UnityEngine.Object value)
        {
#if UNITY_6000_6_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }


        private readonly Dictionary<SpawnObjectId, Queue<GameObject>> _pools = new();
        private readonly Dictionary<SpawnObjectId, int> _poolSizes = new();
        private readonly Dictionary<SpawnObjectId, GameObject> _prefabLookup = new();

        // 注册的生成器
        private readonly List<SpawnerBase> _spawners = new();
        private readonly List<SpawnerBase> _activeSpawners = new();

        // 统计
        private int _totalSpawned;
        private int _totalDespawned;
        private int _poolHits;
        private int _poolMisses;

        #region Properties

        /// <summary>是否启用对象池</summary>
        public bool EnablePooling => _enablePooling;

        /// <summary>注册的生成器数量</summary>
        public int SpawnerCount => _spawners.Count;

        /// <summary>活跃生成器数量</summary>
        public int ActiveSpawnerCount => _activeSpawners.Count;

        /// <summary>对象池命中率</summary>
        public float PoolHitRate => _poolHits + _poolMisses > 0 ?
            (float)_poolHits / (_poolHits + _poolMisses) : 0f;

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();

            // 订阅事件
            SpawnEvents.OnSpawnerActivated += OnSpawnerActivated;
            SpawnEvents.OnSpawnerDeactivated += OnSpawnerDeactivated;
        }

        protected override void OnDestroy()
        {
            SpawnEvents.OnSpawnerActivated -= OnSpawnerActivated;
            SpawnEvents.OnSpawnerDeactivated -= OnSpawnerDeactivated;

            ClearAllPools();
            base.OnDestroy();
        }

        private void LateUpdate()
        {
            // 更新活跃生成器列表
            UpdateActiveSpawners();
        }

        #endregion

        #region Spawner Registration

        /// <summary>
        /// 注册生成器
        /// </summary>
        public void RegisterSpawner(SpawnerBase spawner)
        {
            if (spawner == null || _spawners.Contains(spawner)) return;
            _spawners.Add(spawner);
        }

        /// <summary>
        /// 注销生成器
        /// </summary>
        public void UnregisterSpawner(SpawnerBase spawner)
        {
            _spawners.Remove(spawner);
            _activeSpawners.Remove(spawner);
        }

        /// <summary>
        /// 获取所有生成器
        /// </summary>
        public IReadOnlyList<SpawnerBase> GetAllSpawners() => _spawners;

        /// <summary>
        /// 获取所有活跃生成器
        /// </summary>
        public IReadOnlyList<SpawnerBase> GetActiveSpawners() => _activeSpawners;

        private void OnSpawnerActivated(SpawnerActivateEventArgs args)
        {
            if (!_activeSpawners.Contains(args.Spawner))
            {
                _activeSpawners.Add(args.Spawner);
            }
        }

        private void OnSpawnerDeactivated(SpawnerDeactivateEventArgs args)
        {
            _activeSpawners.Remove(args.Spawner);
        }

        private void UpdateActiveSpawners()
        {
            for (int i = _activeSpawners.Count - 1; i >= 0; i--)
            {
                if (_activeSpawners[i] == null || !_activeSpawners[i].IsActive)
                {
                    _activeSpawners.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Spawn Methods

        /// <summary>
        /// 生成实体
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            GameObject instance = GetFromPool(prefab);

            if (instance == null)
            {
                instance = Instantiate(prefab, position, rotation, transform);
            }
            else
            {
                instance.transform.SetPositionAndRotation(position, rotation);
                instance.SetActive(true);
            }

            _totalSpawned++;
            return instance;
        }

        /// <summary>
        /// 生成实体 (带父级)
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            var instance = Spawn(prefab, position, rotation);
            if (instance != null && parent != null)
            {
                instance.transform.SetParent(parent);
            }
            return instance;
        }

        /// <summary>
        /// 批量生成
        /// </summary>
        public List<GameObject> SpawnBatch(GameObject prefab, int count, Vector3 position, float spacing = 1f)
        {
            var instances = new List<GameObject>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(
                    (i % 5 - 2) * spacing,
                    0,
                    (i / 5) * spacing
                );

                var instance = Spawn(prefab, position + offset, Quaternion.identity);
                if (instance != null)
                {
                    instances.Add(instance);
                }
            }

            return instances;
        }

        /// <summary>
        /// 圆形生成
        /// </summary>
        public List<GameObject> SpawnCircle(GameObject prefab, int count, Vector3 center, float radius)
        {
            var instances = new List<GameObject>(count);

            float angleStep = 360f / count;

            for (int i = 0; i < count; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 pos = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0,
                    Mathf.Sin(angle) * radius
                );

                var instance = Spawn(prefab, pos, Quaternion.LookRotation(center - pos));
                if (instance != null)
                {
                    instances.Add(instance);
                }
            }

            return instances;
        }

        #endregion

        #region Despawn Methods

        /// <summary>
        /// 回收实体
        /// </summary>
        public void Despawn(GameObject instance)
        {
            if (instance == null) return;

            _totalDespawned++;

            if (_enablePooling)
            {
                ReturnToPool(instance);
            }
            else
            {
                Destroy(instance);
            }
        }

        /// <summary>
        /// 延迟回收
        /// </summary>
        public void DespawnDelayed(GameObject instance, float delay)
        {
            if (instance == null) return;
            StartCoroutine(DespawnDelayedCoroutine(instance, delay));
        }

        private System.Collections.IEnumerator DespawnDelayedCoroutine(GameObject instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            Despawn(instance);
        }

        /// <summary>
        /// 回收所有指定预制体的实例
        /// </summary>
        public void DespawnAll(GameObject prefab)
        {
            if (prefab == null) return;

            SpawnObjectId prefabId = GetRuntimeObjectId(prefab);
            var children = new List<Transform>();

            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    // 简单检查：如果名称包含预制体名称
                    if (child.name.Contains(prefab.name))
                    {
                        children.Add(child);
                    }
                }
            }

            foreach (var child in children)
            {
                Despawn(child.gameObject);
            }
        }

        #endregion

        #region Pool Management

        /// <summary>
        /// 预热对象池
        /// </summary>
        public void WarmupPool(GameObject prefab, int count = -1)
        {
            if (prefab == null || !_enablePooling) return;

            int warmupCount = count > 0 ? count : _defaultPoolSize;
            SpawnObjectId prefabId = GetRuntimeObjectId(prefab);

            if (!_pools.ContainsKey(prefabId))
            {
                _pools[prefabId] = new Queue<GameObject>();
                _poolSizes[prefabId] = 0;
                _prefabLookup[prefabId] = prefab;
            }

            for (int i = 0; i < warmupCount; i++)
            {
                if (_poolSizes[prefabId] >= _maxPoolSize) break;

                var instance = Instantiate(prefab, transform);
                instance.SetActive(false);
                instance.name = $"{prefab.name}_Pooled";

                _pools[prefabId].Enqueue(instance);
                _poolSizes[prefabId]++;
            }
        }

        /// <summary>
        /// 从池中获取
        /// </summary>
        private GameObject GetFromPool(GameObject prefab)
        {
            if (!_enablePooling) return null;

            SpawnObjectId prefabId = GetRuntimeObjectId(prefab);

            if (_pools.TryGetValue(prefabId, out var pool) && pool.Count > 0)
            {
                var instance = pool.Dequeue();
                if (instance != null)
                {
                    _poolHits++;
                    return instance;
                }
            }

            _poolMisses++;
            return null;
        }

        /// <summary>
        /// 返回到池
        /// </summary>
        private void ReturnToPool(GameObject instance)
        {
            if (instance == null) return;

            // 尝试找到对应的池
            SpawnObjectId poolKey = default;
            bool foundPool = false;
            foreach (var kvp in _prefabLookup)
            {
                if (instance.name.Contains(kvp.Value.name))
                {
                    poolKey = kvp.Key;
                    foundPool = true;
                    break;
                }
            }

            if (!foundPool)
            {
                // 未找到池，直接销毁
                Destroy(instance);
                return;
            }

            // 检查池大小限制
            if (_poolSizes[poolKey] >= _maxPoolSize)
            {
                Destroy(instance);
                return;
            }

            // 重置并入池
            instance.SetActive(false);
            instance.transform.SetParent(transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            _pools[poolKey].Enqueue(instance);
        }

        /// <summary>
        /// 清理所有池
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    var instance = pool.Dequeue();
                    if (instance != null)
                    {
                        Destroy(instance);
                    }
                }
            }

            _pools.Clear();
            _poolSizes.Clear();
            _prefabLookup.Clear();
        }

        /// <summary>
        /// 获取池状态
        /// </summary>
        public void GetPoolStatus(out int totalPools, out int totalPooled, out int totalActive)
        {
            totalPools = _pools.Count;
            totalPooled = 0;
            totalActive = 0;

            foreach (var pool in _pools.Values)
            {
                totalPooled += pool.Count;
            }

            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    totalActive++;
                }
            }
        }

        #endregion

        #region Global Operations

        /// <summary>
        /// 停用所有生成器
        /// </summary>
        public void DeactivateAllSpawners()
        {
            for (int i = _activeSpawners.Count - 1; i >= 0; i--)
            {
                _activeSpawners[i]?.Deactivate("GlobalDeactivate");
            }
        }

        /// <summary>
        /// 清除所有生成的实体
        /// </summary>
        public void ClearAllEntities()
        {
            foreach (var spawner in _spawners)
            {
                spawner?.ClearAllEntities(DespawnReason.Manual);
            }

            // 清除管理器下的所有实体
            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    Despawn(child.gameObject);
                }
            }
        }

        #endregion

        #region Debug

        private void OnGUI()
        {
            if (!_showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 220, 300, 200));
            GUILayout.BeginVertical("box");

            GUILayout.Label("[SpawnManager Debug]");
            GUILayout.Label($"Spawners: {_spawners.Count} (Active: {_activeSpawners.Count})");
            GUILayout.Label($"Spawned: {_totalSpawned} | Despawned: {_totalDespawned}");
            GUILayout.Label($"Pool Hit Rate: {PoolHitRate:P1}");

            GetPoolStatus(out int pools, out int pooled, out int active);
            GUILayout.Label($"Pools: {pools} | Pooled: {pooled} | Active: {active}");

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        #endregion
    }
}
