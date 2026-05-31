using System;
using UnityEngine;

namespace ZeroEngine.AI.UtilityAI
{
    /// <summary>
    /// 属性值考量 - 与 StatSystem 集成
    /// </summary>
    [Serializable]
    public class StatConsideration : Consideration
    {
        [SerializeField] private string _statId = "";
        [SerializeField] private float _minValue = 0f;
        [SerializeField] private float _maxValue = 100f;
        [SerializeField] private bool _usePercentage = false;

        /// <summary>属性 ID</summary>
        public string StatId
        {
            get => _statId;
            set => _statId = value;
        }

        public StatConsideration()
        {
            _name = "Stat";
        }

        public StatConsideration(string statId, float min = 0f, float max = 100f)
        {
            _statId = statId;
            _minValue = min;
            _maxValue = max;
            _name = $"Stat:{statId}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Owner == null || string.IsNullOrEmpty(_statId))
            {
                return 0f;
            }

            // 尝试从黑板获取 (首选，因为可以被外部更新)
            string key = $"Stat_{_statId}";
            if (context.Blackboard.Contains(key))
            {
                float value = context.Blackboard.GetFloat(key);
                return _usePercentage ? value : Mathf.InverseLerp(_minValue, _maxValue, value);
            }

#if ZEROENGINE_STATSYSTEM
            // 尝试从 StatController 获取
            var statController = context.GetCachedComponent<StatSystem.StatController>();
            if (statController != null && statController.TryGetStat(_statId, out var stat))
            {
                float value = stat.Value;
                return _usePercentage ?
                    Mathf.InverseLerp(stat.MinValue, stat.MaxValue, value) :
                    Mathf.InverseLerp(_minValue, _maxValue, value);
            }
#endif

            return 0f;
        }
    }

    /// <summary>
    /// 生命值考量 - 与 Combat.Health 集成
    /// </summary>
    [Serializable]
    public class HealthConsideration : Consideration
    {
        [SerializeField] private bool _useSelfHealth = true;
#if ZEROENGINE_COMBAT
        [SerializeField] private string _targetBlackboardKey = "";
#endif

        public HealthConsideration()
        {
            _name = "Health";
            // 默认使用反向曲线 (血量低时评分高)
            _responseCurve = ResponseCurve.InverseLinear();
        }

        public HealthConsideration(bool selfHealth, bool inverseCurve = true)
        {
            _useSelfHealth = selfHealth;
            _name = selfHealth ? "SelfHealth" : "TargetHealth";
            _responseCurve = inverseCurve ? ResponseCurve.InverseLinear() : ResponseCurve.Linear();
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context == null) return 0f;

            // 优先从黑板获取
            if (context.Blackboard.Contains(BlackboardKeys.HealthPercent))
            {
                return context.Blackboard.GetFloat(BlackboardKeys.HealthPercent);
            }

#if ZEROENGINE_COMBAT
            GameObject target = _useSelfHealth ? context.Owner :
                (!string.IsNullOrEmpty(_targetBlackboardKey) ?
                    context.Blackboard.GetGameObject(_targetBlackboardKey) :
                    context.CurrentTarget?.gameObject);

            if (target == null) return 0f;

            var health = target.GetComponent<Combat.IHealth>();
            if (health != null)
            {
                return health.HealthPercent;
            }
#endif

            return 1f;
        }
    }

    /// <summary>
    /// Buff 考量 - 与 BuffSystem 集成
    /// </summary>
    [Serializable]
    public class BuffConsideration : Consideration
    {
        [SerializeField] private string _buffId = "";
        [SerializeField] private BuffCheckType _checkType = BuffCheckType.HasBuff;
        [SerializeField] private int _minStacks = 1;

        public enum BuffCheckType
        {
            HasBuff,
            DoesNotHaveBuff,
            StackCount,
            RemainingDuration
        }

        public BuffConsideration()
        {
            _name = "Buff";
        }

        public BuffConsideration(string buffId, BuffCheckType checkType = BuffCheckType.HasBuff)
        {
            _buffId = buffId;
            _checkType = checkType;
            _name = $"Buff:{buffId}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Owner == null || string.IsNullOrEmpty(_buffId))
            {
                return 0f;
            }

#if ZEROENGINE_BUFFSYSTEM
            var buffReceiver = context.GetCachedComponent<BuffSystem.BuffReceiver>();
            if (buffReceiver == null) return 0f;

            bool hasBuff = buffReceiver.HasBuff(_buffId);

            switch (_checkType)
            {
                case BuffCheckType.HasBuff:
                    return hasBuff ? 1f : 0f;

                case BuffCheckType.DoesNotHaveBuff:
                    return hasBuff ? 0f : 1f;

                case BuffCheckType.StackCount:
                    if (!hasBuff) return 0f;
                    var buff = buffReceiver.GetBuff(_buffId);
                    int requiredStacks = Mathf.Max(1, _minStacks);
                    return buff != null ? Mathf.Clamp01((float)buff.CurrentStack / requiredStacks) : 0f;

                case BuffCheckType.RemainingDuration:
                    if (!hasBuff) return 0f;
                    var b = buffReceiver.GetBuff(_buffId);
                    return b != null ? Mathf.Clamp01(b.RemainingDuration / b.Duration) : 0f;

                default:
                    return 0f;
            }
#else
            // 无 BuffSystem 时从黑板读取
            string key = $"Buff_{_buffId}";
            return context.Blackboard.GetBool(key) ? 1f : 0f;
#endif
        }
    }

    /// <summary>
    /// 游戏时间考量 - 与 TimeManager 集成
    /// </summary>
    [Serializable]
    public class TimeConsideration : Consideration
    {
        [SerializeField] private TimeCheckType _checkType = TimeCheckType.Hour;
        [SerializeField] private float _targetHour = 12f;
        [SerializeField] private float _rangeHours = 2f;

        public enum TimeCheckType
        {
            Hour,           // 具体小时
            IsDaytime,      // 是否白天
            IsNighttime,    // 是否夜晚
            HourRange       // 小时范围
        }

        public TimeConsideration()
        {
            _name = "Time";
        }

        public TimeConsideration(TimeCheckType checkType)
        {
            _checkType = checkType;
            _name = $"Time:{checkType}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            float currentHour = 12f;

            // 从黑板获取
            if (context?.Blackboard != null && context.Blackboard.Contains(BlackboardKeys.CurrentHour))
            {
                currentHour = context.Blackboard.GetFloat(BlackboardKeys.CurrentHour);
            }
#if ZEROENGINE_ENVIRONMENT
            else if (EnvironmentSystem.TimeManager.Instance != null)
            {
                currentHour = EnvironmentSystem.TimeManager.Instance.CurrentHour;
            }
#endif

            switch (_checkType)
            {
                case TimeCheckType.Hour:
                    return Mathf.InverseLerp(0f, 24f, currentHour);

                case TimeCheckType.IsDaytime:
                    return (currentHour >= 6f && currentHour < 18f) ? 1f : 0f;

                case TimeCheckType.IsNighttime:
                    return (currentHour < 6f || currentHour >= 18f) ? 1f : 0f;

                case TimeCheckType.HourRange:
                    float diff = Mathf.Abs(currentHour - _targetHour);
                    if (diff > 12f) diff = 24f - diff; // 处理跨午夜
                    return Mathf.Clamp01(1f - (diff / _rangeHours));

                default:
                    return 0f;
            }
        }
    }

    /// <summary>
    /// 战斗状态考量
    /// </summary>
    [Serializable]
    public class CombatStateConsideration : Consideration
    {
        [SerializeField] private CombatStateType _stateType = CombatStateType.InCombat;

        public enum CombatStateType
        {
            InCombat,
            NotInCombat,
            HasTarget,
            NoTarget,
            UnderAttack,
            Alerted
        }

        public CombatStateConsideration()
        {
            _name = "CombatState";
        }

        public CombatStateConsideration(CombatStateType stateType)
        {
            _stateType = stateType;
            _name = $"Combat:{stateType}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context == null) return 0f;

            return _stateType switch
            {
                CombatStateType.InCombat => context.IsInCombat ? 1f : 0f,
                CombatStateType.NotInCombat => context.IsInCombat ? 0f : 1f,
                CombatStateType.HasTarget => context.CurrentTarget != null ? 1f : 0f,
                CombatStateType.NoTarget => context.CurrentTarget == null ? 1f : 0f,
                CombatStateType.UnderAttack => context.IsUnderAttack ? 1f : 0f,
                CombatStateType.Alerted => context.IsAlerted ? 1f : 0f,
                _ => 0f
            };
        }
    }

    /// <summary>
    /// 目标数量考量 - 用于群体战斗决策
    /// </summary>
    [Serializable]
    public class TargetCountConsideration : Consideration
    {
        [SerializeField] private string _targetsBlackboardKey = "NearbyEnemies";
        [SerializeField] private int _minCount = 1;
        [SerializeField] private int _maxCount = 10;

        public TargetCountConsideration()
        {
            _name = "TargetCount";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Blackboard == null) return 0f;

            int count = context.Blackboard.GetInt(_targetsBlackboardKey, 0);
            return Mathf.InverseLerp(_minCount, _maxCount, count);
        }
    }

    /// <summary>
    /// 弹药/资源考量
    /// </summary>
    [Serializable]
    public class ResourceConsideration : Consideration
    {
        [SerializeField] private string _resourceKey = "Ammo";
        [SerializeField] private float _minValue = 0f;
        [SerializeField] private float _maxValue = 100f;

        public ResourceConsideration()
        {
            _name = "Resource";
        }

        public ResourceConsideration(string resourceKey, float min = 0f, float max = 100f)
        {
            _resourceKey = resourceKey;
            _minValue = min;
            _maxValue = max;
            _name = $"Resource:{resourceKey}";
        }

        protected override float CalculateRawValue(AIContext context)
        {
            if (context?.Blackboard == null) return 0f;

            float value = context.Blackboard.GetFloat(_resourceKey, _maxValue);
            return Mathf.InverseLerp(_minValue, _maxValue, value);
        }
    }
}
