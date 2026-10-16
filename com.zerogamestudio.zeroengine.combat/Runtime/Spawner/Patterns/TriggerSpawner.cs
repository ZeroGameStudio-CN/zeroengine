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
    /// 触发生成器 - 基于触发器或事件生成
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TriggerSpawner : SpawnerBase
    {
        [Header("Trigger Settings")]
        [SerializeField] private TriggerMode _triggerMode = TriggerMode.OnEnter;
        [SerializeField] private LayerMask _triggerLayers = 1;
        [SerializeField] private string _triggerTag = "";

        [Header("Trigger Behavior")]
        [SerializeField] private int _maxTriggerCount = 0; // 0 = unlimited
        [SerializeField] private float _triggerCooldown = 1f;
        [SerializeField] private bool _spawnOnce = false;
        [SerializeField] private bool _requireAllConditions = false;

        [Header("Spawn On Trigger")]
        [SerializeField] private int _spawnCountOnTrigger = 1;
        [SerializeField] private bool _spawnAtTriggerPosition = false;

        [Header("Conditions")]
        [SerializeField] private List<SpawnConditionBase> _conditions = new();

        // 触发状态
        private int _triggerCount;
        private float _lastTriggerTime;
        private bool _hasTriggered;
        private HashSet<SpawnObjectId> _entitiesInTrigger = new();

        private static SpawnObjectId GetRuntimeObjectId(UnityEngine.Object value)
        {
#if UNITY_6000_6_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }



        #region Properties

        /// <summary>触发次数</summary>
        public int TriggerCount => _triggerCount;

        /// <summary>是否已触发 (仅限 SpawnOnce 模式)</summary>
        public bool HasTriggered => _hasTriggered;

        /// <summary>触发器内实体数量</summary>
        public int EntitiesInTrigger => _entitiesInTrigger.Count;

        /// <summary>是否可以触发</summary>
        public bool CanTrigger => !_hasTriggered || !_spawnOnce;

        #endregion

        #region Unity Lifecycle

        protected override void Start()
        {
            // 确保碰撞体是触发器
            var collider = GetComponent<Collider>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }

            // 不自动激活，等待触发
            _isActive = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsValidTrigger(other)) return;

            _entitiesInTrigger.Add(GetRuntimeObjectId(other));

            if (_triggerMode == TriggerMode.OnEnter || _triggerMode == TriggerMode.OnEnterOrExit)
            {
                TryTriggerSpawn(other.transform.position);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsValidTrigger(other)) return;

            _entitiesInTrigger.Remove(GetRuntimeObjectId(other));

            if (_triggerMode == TriggerMode.OnExit || _triggerMode == TriggerMode.OnEnterOrExit)
            {
                TryTriggerSpawn(other.transform.position);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsValidTrigger(other)) return;

            if (_triggerMode == TriggerMode.WhileInside)
            {
                TryTriggerSpawn(other.transform.position);
            }
        }

        #endregion

        #region Trigger Logic

        /// <summary>
        /// 检查是否为有效触发者
        /// </summary>
        private bool IsValidTrigger(Collider other)
        {
            // 层级检查
            int layer = 1 << other.gameObject.layer;
            if ((_triggerLayers & layer) == 0)
            {
                return false;
            }

            // 标签检查
            if (!string.IsNullOrEmpty(_triggerTag) && !other.CompareTag(_triggerTag))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 尝试触发生成
        /// </summary>
        private void TryTriggerSpawn(Vector3 triggerPosition)
        {
            // 检查单次触发
            if (_spawnOnce && _hasTriggered)
            {
                return;
            }

            // 检查触发次数限制
            if (_maxTriggerCount > 0 && _triggerCount >= _maxTriggerCount)
            {
                return;
            }

            // 检查冷却
            if (Time.time - _lastTriggerTime < _triggerCooldown)
            {
                return;
            }

            // 检查条件
            if (!CheckConditions())
            {
                return;
            }

            // 执行生成
            DoTriggerSpawn(triggerPosition);

            _triggerCount++;
            _lastTriggerTime = Time.time;
            _hasTriggered = true;
        }

        /// <summary>
        /// 检查所有条件
        /// </summary>
        private bool CheckConditions()
        {
            if (_conditions.Count == 0) return true;

            if (_requireAllConditions)
            {
                // 所有条件都必须满足
                foreach (var condition in _conditions)
                {
                    if (condition != null && !condition.IsMet())
                    {
                        return false;
                    }
                }
                return true;
            }
            else
            {
                // 任一条件满足即可
                foreach (var condition in _conditions)
                {
                    if (condition != null && condition.IsMet())
                    {
                        return true;
                    }
                }
                return _conditions.Count == 0;
            }
        }

        /// <summary>
        /// 执行触发生成
        /// </summary>
        private void DoTriggerSpawn(Vector3 triggerPosition)
        {
            var entry = GetNextEntry();
            if (entry == null) return;

            Vector3 spawnPos = _spawnAtTriggerPosition ? triggerPosition : transform.position;
            Quaternion spawnRot = transform.rotation;

            for (int i = 0; i < _spawnCountOnTrigger; i++)
            {
                DoSpawn(entry, spawnPos, spawnRot);
            }
        }

        #endregion

        #region Spawn Logic

        protected override void UpdateSpawnLogic(float deltaTime)
        {
            // TriggerSpawner 主要依靠触发器事件，不使用定时生成
            // 但可以在 WhileInside 模式下持续生成

            if (_triggerMode == TriggerMode.WhileInside && _entitiesInTrigger.Count > 0)
            {
                _spawnTimer -= deltaTime;

                if (_spawnTimer <= 0f && CanSpawn())
                {
                    var entry = GetNextEntry();
                    if (entry != null)
                    {
                        DoSpawn(entry, transform.position, transform.rotation);
                    }

                    _spawnTimer = _spawnData?.GetActualInterval() ?? 1f;
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 手动触发生成
        /// </summary>
        public void ManualTrigger()
        {
            TryTriggerSpawn(transform.position);
        }

        /// <summary>
        /// 重置触发器状态
        /// </summary>
        public void ResetTrigger()
        {
            _triggerCount = 0;
            _hasTriggered = false;
            _lastTriggerTime = 0f;
            _entitiesInTrigger.Clear();
        }

        /// <summary>
        /// 添加条件
        /// </summary>
        public void AddCondition(SpawnConditionBase condition)
        {
            if (condition != null && !_conditions.Contains(condition))
            {
                _conditions.Add(condition);
            }
        }

        /// <summary>
        /// 移除条件
        /// </summary>
        public void RemoveCondition(SpawnConditionBase condition)
        {
            _conditions.Remove(condition);
        }

        /// <summary>
        /// 检查实体是否在触发区内
        /// </summary>
        public bool IsEntityInTrigger(GameObject entity)
        {
            if (entity == null) return false;

            var collider = entity.GetComponent<Collider>();
            return collider != null && _entitiesInTrigger.Contains(GetRuntimeObjectId(collider));
        }

        #endregion

        #region Gizmos

        protected override void OnDrawGizmos()
        {
            // 绘制触发区域
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = _hasTriggered ? Color.gray : (_isActive ? Color.green : Color.yellow);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.3f);

            if (collider is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (collider is SphereCollider sphere)
            {
                Gizmos.DrawSphere(transform.position + sphere.center, sphere.radius);
                Gizmos.DrawWireSphere(transform.position + sphere.center, sphere.radius);
            }
            else if (collider is CapsuleCollider capsule)
            {
                Gizmos.DrawWireSphere(transform.position + capsule.center, capsule.radius);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }

        #endregion
    }

    /// <summary>
    /// 触发模式
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>进入时触发</summary>
        OnEnter,
        /// <summary>离开时触发</summary>
        OnExit,
        /// <summary>进入或离开时触发</summary>
        OnEnterOrExit,
        /// <summary>停留时持续触发</summary>
        WhileInside
    }
}
