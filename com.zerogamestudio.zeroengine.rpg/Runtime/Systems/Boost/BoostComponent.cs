using System;
using UnityEngine;
using ZeroEngine.Utils;

namespace ZeroEngine.RPG.Systems
{
    /// <summary>
    /// BP 强化组件 - 八方旅人风格
    /// 附加到战斗单位上以启用 BP 系统
    /// </summary>
    public class BoostComponent : MonoBehaviour, IBoostable
    {
        #region 序列化字段

        [Header("BP 配置")]
        [SerializeField, Tooltip("最大 BP 点数")]
        private int _maxBP = BoostConstants.DEFAULT_MAX_BP;

        [SerializeField, Tooltip("每回合恢复 BP")]
        private int _bpPerTurn = BoostConstants.DEFAULT_BP_PER_TURN;

        [SerializeField, Tooltip("战斗开始时的初始 BP")]
        private int _initialBP = 0;

        [Header("状态")]
        [SerializeField, Tooltip("当前 BP (只读)")]
        private int _currentBP;

        #endregion

        #region IBoostable 实现

        /// <summary>
        /// 当前 BP 点数
        /// </summary>
        public int CurrentBP => _currentBP;

        /// <summary>
        /// 最大 BP 点数
        /// </summary>
        public int MaxBP => _maxBP;

        /// <summary>
        /// 每回合恢复的 BP
        /// </summary>
        public int BPPerTurn => _bpPerTurn;

        /// <summary>
        /// 是否可以使用 BP (至少有 1 点)
        /// </summary>
        public bool CanBoost => _currentBP >= 1;

        /// <summary>
        /// BP 变化事件
        /// </summary>
        public event Action<int, int> OnBPChanged;

        /// <summary>
        /// 消耗 BP
        /// </summary>
        /// <param name="amount">消耗数量 (1-3)</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeBP(int amount)
        {
            if (amount <= 0 || amount > BoostConstants.MAX_BOOST_LEVEL)
            {
                Debug.LogWarning($"[BoostComponent] 无效的 BP 消耗数量: {amount}，有效范围: 1-{BoostConstants.MAX_BOOST_LEVEL}");
                return false;
            }

            if (_currentBP < amount)
            {
                DebugUtils.Log($"[BoostComponent] BP 不足，需要 {amount}，当前 {_currentBP}");
                return false;
            }

            int oldBP = _currentBP;
            _currentBP -= amount;

            DebugUtils.Log($"[BoostComponent] 消耗 {amount} BP: {oldBP} -> {_currentBP}");
            OnBPChanged?.Invoke(oldBP, _currentBP);

            return true;
        }

        /// <summary>
        /// 恢复 BP
        /// </summary>
        /// <param name="amount">恢复数量</param>
        public void RecoverBP(int amount)
        {
            if (amount <= 0) return;

            int oldBP = _currentBP;
            _currentBP = Mathf.Min(_currentBP + amount, _maxBP);

            if (_currentBP != oldBP)
            {
            DebugUtils.Log($"[BoostComponent] 恢复 {amount} BP: {oldBP} -> {_currentBP}");
                OnBPChanged?.Invoke(oldBP, _currentBP);
            }
        }

        /// <summary>
        /// 回合开始时的 BP 恢复
        /// </summary>
        public void OnTurnStartBPRecovery()
        {
            RecoverBP(_bpPerTurn);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化 BP (战斗开始时调用)
        /// </summary>
        public void Initialize()
        {
            int oldBP = _currentBP;
            _currentBP = Mathf.Clamp(_initialBP, 0, _maxBP);

            if (_currentBP != oldBP)
            {
                OnBPChanged?.Invoke(oldBP, _currentBP);
            }

            DebugUtils.Log($"[BoostComponent] 初始化 BP: {_currentBP}/{_maxBP}");
        }

        /// <summary>
        /// 重置 BP 到满值
        /// </summary>
        public void ResetToFull()
        {
            int oldBP = _currentBP;
            _currentBP = _maxBP;

            if (_currentBP != oldBP)
            {
                OnBPChanged?.Invoke(oldBP, _currentBP);
            }
        }

        /// <summary>
        /// 设置最大 BP (运行时调整)
        /// </summary>
        /// <param name="newMaxBP">新的最大 BP</param>
        public void SetMaxBP(int newMaxBP)
        {
            _maxBP = Mathf.Max(1, newMaxBP);

            // 确保当前 BP 不超过新上限
            if (_currentBP > _maxBP)
            {
                int oldBP = _currentBP;
                _currentBP = _maxBP;
                OnBPChanged?.Invoke(oldBP, _currentBP);
            }
        }

        /// <summary>
        /// 获取可用的最大 Boost 等级
        /// </summary>
        /// <returns>当前可用的最大 Boost 等级 (0-3)</returns>
        public int GetAvailableBoostLevel()
        {
            return Mathf.Min(_currentBP, BoostConstants.MAX_BOOST_LEVEL);
        }

        /// <summary>
        /// 检查是否有足够的 BP 进行指定等级的 Boost
        /// </summary>
        /// <param name="boostLevel">Boost 等级</param>
        /// <returns>是否有足够 BP</returns>
        public bool HasEnoughBP(int boostLevel)
        {
            return _currentBP >= boostLevel;
        }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 初始化 BP 值
            _currentBP = Mathf.Clamp(_initialBP, 0, _maxBP);
        }

        private void OnValidate()
        {
            _maxBP = Mathf.Max(1, _maxBP);
            _bpPerTurn = Mathf.Max(0, _bpPerTurn);
            _initialBP = Mathf.Clamp(_initialBP, 0, _maxBP);
            _currentBP = Mathf.Clamp(_currentBP, 0, _maxBP);
        }

        #endregion
    }
}
