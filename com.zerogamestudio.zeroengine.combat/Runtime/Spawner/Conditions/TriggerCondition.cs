using UnityEngine;

namespace ZeroEngine.Spawner
{
    /// <summary>
    /// 触发条件 - 基于游戏事件或状态的触发条件
    /// </summary>
    public class TriggerCondition : SpawnConditionBase
    {
        [Header("Trigger Settings")]
        [SerializeField] private TriggerConditionType _conditionType = TriggerConditionType.Manual;
        [SerializeField] private string _eventTag = "";

        [Header("State Trigger")]
        [SerializeField] private GameObject _targetObject;
        [SerializeField] private string _targetState = "";

        [Header("Distance Trigger")]
        [SerializeField] private Transform _distanceTarget;
        [SerializeField] private float _triggerDistance = 10f;
        [SerializeField] private DistanceComparison _distanceComparison = DistanceComparison.LessThan;

        [Header("Health Trigger")]
        [SerializeField] private Combat.IHealth _healthTarget;
        [SerializeField] private float _healthThreshold = 0.5f;
        [SerializeField] private HealthComparison _healthComparison = HealthComparison.LessThan;

        // 运行时状态
        private bool _isTriggered;
        private bool _latchTrigger; // 锁定触发状态

        #region Properties

        /// <summary>是否已触发</summary>
        public bool IsTriggered => _isTriggered;

        /// <summary>条件类型</summary>
        public TriggerConditionType ConditionType
        {
            get => _conditionType;
            set => _conditionType = value;
        }

        #endregion

        #region Condition Logic

        protected override bool CheckCondition()
        {
            // 如果已锁定，直接返回
            if (_latchTrigger && _isTriggered)
            {
                return true;
            }

            bool result = _conditionType switch
            {
                TriggerConditionType.Manual => _isTriggered,
                TriggerConditionType.ObjectActive => CheckObjectActive(),
                TriggerConditionType.ObjectInactive => !CheckObjectActive(),
                TriggerConditionType.Distance => CheckDistance(),
                TriggerConditionType.Health => CheckHealth(),
                TriggerConditionType.Tag => CheckTag(),
                TriggerConditionType.Always => true,
                TriggerConditionType.Never => false,
                _ => false
            };

            _isTriggered = result;
            return result;
        }

        /// <summary>
        /// 检查对象激活状态
        /// </summary>
        private bool CheckObjectActive()
        {
            return _targetObject != null && _targetObject.activeInHierarchy;
        }

        /// <summary>
        /// 检查距离
        /// </summary>
        private bool CheckDistance()
        {
            if (_distanceTarget == null) return false;

            float distance = Vector3.Distance(transform.position, _distanceTarget.position);

            return _distanceComparison switch
            {
                DistanceComparison.LessThan => distance < _triggerDistance,
                DistanceComparison.LessThanOrEqual => distance <= _triggerDistance,
                DistanceComparison.GreaterThan => distance > _triggerDistance,
                DistanceComparison.GreaterThanOrEqual => distance >= _triggerDistance,
                DistanceComparison.Equal => Mathf.Approximately(distance, _triggerDistance),
                _ => false
            };
        }

        /// <summary>
        /// 检查生命值
        /// </summary>
        private bool CheckHealth()
        {
            if (_healthTarget == null)
            {
                // 尝试从目标对象获取
                if (_targetObject != null)
                {
                    _healthTarget = _targetObject.GetComponent<Combat.IHealth>();
                }

                if (_healthTarget == null) return false;
            }

            float healthPercent = _healthTarget.CurrentHealth / _healthTarget.MaxHealth;

            return _healthComparison switch
            {
                HealthComparison.LessThan => healthPercent < _healthThreshold,
                HealthComparison.LessThanOrEqual => healthPercent <= _healthThreshold,
                HealthComparison.GreaterThan => healthPercent > _healthThreshold,
                HealthComparison.GreaterThanOrEqual => healthPercent >= _healthThreshold,
                HealthComparison.Equal => Mathf.Approximately(healthPercent, _healthThreshold),
                _ => false
            };
        }

        /// <summary>
        /// 检查标签
        /// </summary>
        private bool CheckTag()
        {
            string tag = !string.IsNullOrEmpty(_eventTag) ? _eventTag : _targetState;
            if (_targetObject == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }

            return _targetObject.CompareTag(tag);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 手动触发
        /// </summary>
        public void Trigger()
        {
            _isTriggered = true;
        }

        /// <summary>
        /// 取消触发
        /// </summary>
        public void Untrigger()
        {
            if (!_latchTrigger)
            {
                _isTriggered = false;
            }
        }

        /// <summary>
        /// 切换触发状态
        /// </summary>
        public void Toggle()
        {
            if (_latchTrigger && _isTriggered) return;
            _isTriggered = !_isTriggered;
        }

        /// <summary>
        /// 设置锁定模式 (触发后不可取消)
        /// </summary>
        public void SetLatch(bool latch)
        {
            _latchTrigger = latch;
        }

        /// <summary>
        /// 重置条件
        /// </summary>
        public override void ResetCondition()
        {
            _isTriggered = false;
            _latchTrigger = false;
        }

        /// <summary>
        /// 设置距离触发
        /// </summary>
        public void SetDistanceTrigger(Transform target, float distance, DistanceComparison comparison = DistanceComparison.LessThan)
        {
            _conditionType = TriggerConditionType.Distance;
            _distanceTarget = target;
            _triggerDistance = distance;
            _distanceComparison = comparison;
        }

        /// <summary>
        /// 设置生命值触发
        /// </summary>
        public void SetHealthTrigger(Combat.IHealth target, float threshold, HealthComparison comparison = HealthComparison.LessThan)
        {
            _conditionType = TriggerConditionType.Health;
            _healthTarget = target;
            _healthThreshold = threshold;
            _healthComparison = comparison;
        }

        /// <summary>
        /// 设置对象状态触发
        /// </summary>
        public void SetObjectTrigger(GameObject target, bool activeState = true)
        {
            _conditionType = activeState ? TriggerConditionType.ObjectActive : TriggerConditionType.ObjectInactive;
            _targetObject = target;
        }

        /// <summary>
        /// 设置标签触发
        /// </summary>
        public void SetTagTrigger(GameObject target, string tag)
        {
            _conditionType = TriggerConditionType.Tag;
            _targetObject = target;
            _eventTag = tag;
        }

        public override string GetDescription()
        {
            return _conditionType switch
            {
                TriggerConditionType.Manual => "Manual trigger",
                TriggerConditionType.ObjectActive => $"{_targetObject?.name} is active",
                TriggerConditionType.ObjectInactive => $"{_targetObject?.name} is inactive",
                TriggerConditionType.Distance => $"Distance {_distanceComparison} {_triggerDistance}",
                TriggerConditionType.Health => $"Health {_healthComparison} {_healthThreshold:P0}",
                TriggerConditionType.Tag => $"Tag equals '{(!string.IsNullOrEmpty(_eventTag) ? _eventTag : _targetState)}'",
                TriggerConditionType.Always => "Always",
                TriggerConditionType.Never => "Never",
                _ => base.GetDescription()
            };
        }

        public override float GetProgress()
        {
            if (_conditionType == TriggerConditionType.Distance && _distanceTarget != null)
            {
                float distance = Vector3.Distance(transform.position, _distanceTarget.position);
                return Mathf.Clamp01(1f - (distance / _triggerDistance));
            }

            if (_conditionType == TriggerConditionType.Health && _healthTarget != null)
            {
                float healthPercent = _healthTarget.CurrentHealth / _healthTarget.MaxHealth;
                return _healthComparison == HealthComparison.LessThan ?
                    Mathf.Clamp01(1f - (healthPercent / _healthThreshold)) :
                    Mathf.Clamp01(healthPercent / _healthThreshold);
            }

            return _isTriggered ? 1f : 0f;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            // 绘制距离触发范围
            if (_conditionType == TriggerConditionType.Distance)
            {
                Gizmos.color = _isTriggered ? Color.green : Color.yellow;
                Gizmos.DrawWireSphere(transform.position, _triggerDistance);

                if (_distanceTarget != null)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(transform.position, _distanceTarget.position);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// 触发条件类型
    /// </summary>
    public enum TriggerConditionType
    {
        /// <summary>手动触发</summary>
        Manual,
        /// <summary>对象激活时</summary>
        ObjectActive,
        /// <summary>对象未激活时</summary>
        ObjectInactive,
        /// <summary>距离条件</summary>
        Distance,
        /// <summary>生命值条件</summary>
        Health,
        /// <summary>标签匹配</summary>
        Tag,
        /// <summary>总是满足</summary>
        Always,
        /// <summary>永不满足</summary>
        Never
    }

    /// <summary>
    /// 距离比较方式
    /// </summary>
    public enum DistanceComparison
    {
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Equal
    }

    /// <summary>
    /// 生命值比较方式
    /// </summary>
    public enum HealthComparison
    {
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
        Equal
    }
}
