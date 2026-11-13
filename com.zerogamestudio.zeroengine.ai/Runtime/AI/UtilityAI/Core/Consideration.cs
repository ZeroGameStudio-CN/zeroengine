using System;
using UnityEngine;

namespace ZeroEngine.AI.UtilityAI
{
    /// <summary>
    /// 考量因素基类
    /// 负责评估某个方面的效用值 (0-1)
    /// </summary>
    [Serializable]
    public abstract class Consideration
    {
        #region Serialized Fields

        [SerializeField] protected string _name = "";
        [SerializeField] protected string _description = "";
        [SerializeField] protected float _weight = 1f;
        [SerializeField] protected ResponseCurve _responseCurve = new();
        [SerializeField] protected bool _isEnabled = true;

        #endregion

        #region Properties

        /// <summary>考量名称</summary>
        public string Name
        {
            get => string.IsNullOrEmpty(_name) ? GetType().Name : _name;
            set => _name = value;
        }

        /// <summary>描述</summary>
        public string Description
        {
            get => _description;
            set => _description = value;
        }

        /// <summary>权重 (0-1+)</summary>
        public float Weight
        {
            get => _weight;
            set => _weight = Mathf.Max(0f, value);
        }

        /// <summary>响应曲线</summary>
        public ResponseCurve Curve
        {
            get => _responseCurve;
            set => _responseCurve = value ?? new ResponseCurve();
        }

        /// <summary>是否启用</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <summary>最后计算的原始值</summary>
        public float LastRawValue { get; protected set; }

        /// <summary>最后计算的评分</summary>
        public float LastScore { get; protected set; }

        #endregion

        #region Abstract Methods

        /// <summary>
        /// 计算原始输入值 (0-1)
        /// </summary>
        protected abstract float CalculateRawValue(AIContext context);

        #endregion

        #region Evaluation

        /// <summary>
        /// 计算最终评分
        /// </summary>
        public float Evaluate(AIContext context)
        {
            if (!_isEnabled)
            {
                LastRawValue = 0f;
                LastScore = 0f;
                return 0f;
            }

            // 计算原始值
            LastRawValue = Mathf.Clamp01(CalculateRawValue(context));

            // 应用响应曲线
            float curveValue = _responseCurve.Evaluate(LastRawValue);

            // 应用权重
            LastScore = curveValue * _weight;

            return LastScore;
        }

        #endregion

        #region Utility

        /// <summary>
        /// 创建深拷贝
        /// </summary>
        public virtual Consideration Clone()
        {
            var clone = (Consideration)MemberwiseClone();
            clone._responseCurve = new ResponseCurve
            {
                Type = _responseCurve.Type,
                Slope = _responseCurve.Slope,
                Exponent = _responseCurve.Exponent,
                Invert = _responseCurve.Invert
            };
            return clone;
        }

        public override string ToString()
        {
            return $"{Name}: {LastScore:F2} (raw: {LastRawValue:F2}, weight: {_weight:F2})";
        }

        #endregion
    }

    /// <summary>
    /// 固定值考量 - 始终返回固定分数
    /// </summary>
    [Serializable]
    public class FixedConsideration : Consideration
    {
        [SerializeField] private float _fixedValue = 1f;

        public float FixedValue
        {
            get => _fixedValue;
            set => _fixedValue = Mathf.Clamp01(value);
        }

        public FixedConsideration() { }

        public FixedConsideration(float value)
        {
            _fixedValue = Mathf.Clamp01(value);
            _name = $"Fixed({value:F2})";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            return _fixedValue;
        }
    }

    /// <summary>
    /// 黑板值考量 - 从黑板读取值
    /// </summary>
    [Serializable]
    public class BlackboardConsideration : Consideration
    {
        [SerializeField] private string _blackboardKey = "";
        [SerializeField] private float _minValue = 0f;
        [SerializeField] private float _maxValue = 1f;

        public string BlackboardKey
        {
            get => _blackboardKey;
            set => _blackboardKey = value;
        }

        public BlackboardConsideration() { }

        public BlackboardConsideration(string key, float min = 0f, float max = 1f)
        {
            _blackboardKey = key;
            _minValue = min;
            _maxValue = max;
            _name = $"BB:{key}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Blackboard == null || string.IsNullOrEmpty(_blackboardKey))
            {
                return 0f;
            }

            float value = context.Blackboard.GetFloat(_blackboardKey);
            return Mathf.InverseLerp(_minValue, _maxValue, value);
        }
    }

    /// <summary>
    /// 距离考量 - 基于到目标的距离
    /// </summary>
    [Serializable]
    public class DistanceConsideration : Consideration
    {
        [SerializeField] private float _minDistance = 0f;
        [SerializeField] private float _maxDistance = 20f;
        [SerializeField] private bool _useTargetPosition = false;

        public DistanceConsideration()
        {
            _name = "Distance";
            _responseCurve = ResponseCurve.InverseLinear();
        }

        public DistanceConsideration(float minDist, float maxDist)
        {
            _name = "Distance";
            _minDistance = minDist;
            _maxDistance = maxDist;
            _responseCurve = ResponseCurve.InverseLinear();
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context == null) return 0f;

            float distance;
            if (_useTargetPosition || context.CurrentTarget == null)
            {
                distance = Vector3.Distance(context.Transform.position, context.TargetPosition);
            }
            else
            {
                distance = context.DistanceToTarget;
            }

            return Mathf.InverseLerp(_minDistance, _maxDistance, distance);
        }
    }

    /// <summary>
    /// 冷却考量 - 基于时间冷却
    /// </summary>
    [Serializable]
    public class CooldownConsideration : Consideration
    {
        [SerializeField] private string _cooldownKey = "";
        [SerializeField] private float _cooldownDuration = 5f;

        public CooldownConsideration()
        {
            _name = "Cooldown";
        }

        public CooldownConsideration(string key, float duration)
        {
            _cooldownKey = key;
            _cooldownDuration = duration;
            _name = $"Cooldown:{key}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Blackboard == null || string.IsNullOrEmpty(_cooldownKey))
            {
                return 1f;
            }

            float lastUseTime = context.Blackboard.GetFloat(_cooldownKey);
            if (lastUseTime <= 0f) return 1f;

            float elapsed = context.Now - lastUseTime;
            return Mathf.Clamp01(elapsed / _cooldownDuration);
        }
    }

    /// <summary>
    /// 随机考量 - 添加随机性
    /// </summary>
    [Serializable]
    public class RandomConsideration : Consideration
    {
        [SerializeField] private float _minValue = 0f;
        [SerializeField] private float _maxValue = 1f;
        [SerializeField] private float _refreshInterval = 1f;

        private float _cachedValue;
        private float _lastRefreshTime;

        public RandomConsideration()
        {
            _name = "Random";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context.Now - _lastRefreshTime >= _refreshInterval)
            {
                _cachedValue = UnityEngine.Random.Range(_minValue, _maxValue);
                _lastRefreshTime = context.Now;
            }

            return _cachedValue;
        }
    }
}
