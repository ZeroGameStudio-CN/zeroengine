using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Utils;

namespace ZeroEngine.RPG.Systems
{
    /// <summary>
    /// 护盾组件 - 八方旅人风格破盾系统
    /// 附加到战斗单位上以启用破盾机制
    /// </summary>
    public class ShieldComponent : MonoBehaviour, IShieldable
    {
        #region 序列化字段

        [Header("护盾配置")]
        [SerializeField, Tooltip("最大护盾点数")]
        private int _maxShield = ShieldConstants.DEFAULT_SHIELD_POINTS;

        [SerializeField, Tooltip("破盾恢复回合数")]
        private int _breakRecoveryTurns = ShieldConstants.BREAK_RECOVERY_TURNS;

        [Header("弱点配置")]
        [SerializeField, Tooltip("弱点类型列表")]
        private List<WeaknessType> _weaknesses = new();

        [Header("状态 (只读)")]
        [SerializeField]
        private int _currentShield;

        [SerializeField]
        private bool _isBroken;

        [SerializeField]
        private int _brokenTurnsRemaining;

        #endregion

        #region IShieldable 实现

        /// <summary>
        /// 当前护盾点数
        /// </summary>
        public int ShieldPoints => _currentShield;

        /// <summary>
        /// 最大护盾点数
        /// </summary>
        public int MaxShield => _maxShield;

        /// <summary>
        /// 弱点列表 (只读)
        /// </summary>
        public IReadOnlyCollection<WeaknessType> Weaknesses => _weaknesses.AsReadOnly();

        /// <summary>
        /// 是否处于破盾状态
        /// </summary>
        public bool IsBroken => _isBroken;

        /// <summary>
        /// 破盾状态剩余回合数
        /// </summary>
        public int BrokenTurnsRemaining => _brokenTurnsRemaining;

        /// <summary>
        /// 护盾点数变化事件
        /// </summary>
        public event Action<int, int> OnShieldChanged;

        /// <summary>
        /// 破盾事件
        /// </summary>
        public event Action OnBroken;

        /// <summary>
        /// 从破盾恢复事件
        /// </summary>
        public event Action OnRecovered;

        /// <summary>
        /// 弱点命中事件
        /// </summary>
        public event Action<WeaknessType, int> OnWeaknessHit;

        /// <summary>
        /// 检查是否对指定攻击类型有弱点
        /// </summary>
        public bool CheckWeakness(WeaknessType attackType)
        {
            if (attackType == WeaknessType.None) return false;

            foreach (var weakness in _weaknesses)
            {
                if (weakness.HasWeakness(attackType))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 对护盾造成伤害
        /// </summary>
        public bool TakeShieldDamage(int amount)
        {
            if (_isBroken || amount <= 0) return false;

            int oldShield = _currentShield;
            _currentShield = Mathf.Max(0, _currentShield - amount);

            DebugUtils.Log($"[ShieldComponent] 护盾受损: {oldShield} -> {_currentShield}");
            OnShieldChanged?.Invoke(oldShield, _currentShield);

            // 检查是否触发破盾
            if (_currentShield <= 0)
            {
                Break();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 触发破盾
        /// </summary>
        public void Break()
        {
            if (_isBroken) return;

            _isBroken = true;
            _brokenTurnsRemaining = _breakRecoveryTurns;

            DebugUtils.Log($"[ShieldComponent] 破盾！恢复需要 {_brokenTurnsRemaining} 回合");
            OnBroken?.Invoke();
        }

        /// <summary>
        /// 从破盾状态恢复
        /// </summary>
        public void RecoverFromBreak()
        {
            if (!_isBroken) return;

            _isBroken = false;
            _brokenTurnsRemaining = 0;

            // 恢复护盾到满值
            int oldShield = _currentShield;
            _currentShield = _maxShield;

            DebugUtils.Log($"[ShieldComponent] 从破盾状态恢复，护盾: {oldShield} -> {_currentShield}");

            OnShieldChanged?.Invoke(oldShield, _currentShield);
            OnRecovered?.Invoke();
        }

        /// <summary>
        /// 回合结束时处理破盾恢复
        /// </summary>
        public void OnTurnEndShieldRecovery()
        {
            if (!_isBroken) return;

            _brokenTurnsRemaining--;
            DebugUtils.Log($"[ShieldComponent] 破盾恢复倒计时: {_brokenTurnsRemaining}");

            if (_brokenTurnsRemaining <= 0)
            {
                RecoverFromBreak();
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化护盾 (战斗开始时调用)
        /// </summary>
        public void Initialize()
        {
            _currentShield = _maxShield;
            _isBroken = false;
            _brokenTurnsRemaining = 0;

            DebugUtils.Log($"[ShieldComponent] 初始化护盾: {_currentShield}/{_maxShield}, 弱点数: {_weaknesses.Count}");
        }

        /// <summary>
        /// 处理弱点攻击
        /// </summary>
        /// <param name="attackType">攻击类型</param>
        /// <param name="shieldDamage">护盾伤害量</param>
        /// <returns>是否命中弱点</returns>
        public bool ProcessWeaknessHit(WeaknessType attackType, int shieldDamage = ShieldConstants.SHIELD_DAMAGE_PER_WEAKNESS_HIT)
        {
            bool isWeakness = CheckWeakness(attackType);

            if (isWeakness && !_isBroken)
            {
                OnWeaknessHit?.Invoke(attackType, shieldDamage);
                TakeShieldDamage(shieldDamage);
            }

            return isWeakness;
        }

        /// <summary>
        /// 添加弱点
        /// </summary>
        public void AddWeakness(WeaknessType weakness)
        {
            if (!_weaknesses.Contains(weakness))
            {
                _weaknesses.Add(weakness);
            }
        }

        /// <summary>
        /// 移除弱点
        /// </summary>
        public void RemoveWeakness(WeaknessType weakness)
        {
            _weaknesses.Remove(weakness);
        }

        /// <summary>
        /// 设置弱点列表
        /// </summary>
        public void SetWeaknesses(IEnumerable<WeaknessType> weaknesses)
        {
            _weaknesses.Clear();
            _weaknesses.AddRange(weaknesses);
        }

        /// <summary>
        /// 设置最大护盾 (运行时调整)
        /// </summary>
        public void SetMaxShield(int newMaxShield)
        {
            _maxShield = Mathf.Max(1, newMaxShield);

            // 如果不在破盾状态，调整当前护盾
            if (!_isBroken && _currentShield > _maxShield)
            {
                int oldShield = _currentShield;
                _currentShield = _maxShield;
                OnShieldChanged?.Invoke(oldShield, _currentShield);
            }
        }

        /// <summary>
        /// 恢复指定数量的护盾点数
        /// </summary>
        public void RestoreShield(int amount)
        {
            if (_isBroken || amount <= 0) return;

            int oldShield = _currentShield;
            _currentShield = Mathf.Min(_currentShield + amount, _maxShield);

            if (_currentShield != oldShield)
            {
                OnShieldChanged?.Invoke(oldShield, _currentShield);
            }
        }

        /// <summary>
        /// 获取弱点类型的位掩码形式
        /// </summary>
        public WeaknessType GetWeaknessMask()
        {
            WeaknessType mask = WeaknessType.None;
            foreach (var weakness in _weaknesses)
            {
                mask |= weakness;
            }
            return mask;
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            _currentShield = _maxShield;
        }

        private void OnValidate()
        {
            _maxShield = Mathf.Clamp(_maxShield, 1, ShieldConstants.MAX_SHIELD_POINTS);
            _breakRecoveryTurns = Mathf.Max(0, _breakRecoveryTurns);
            _currentShield = Mathf.Clamp(_currentShield, 0, _maxShield);
        }

        #endregion
    }
}
