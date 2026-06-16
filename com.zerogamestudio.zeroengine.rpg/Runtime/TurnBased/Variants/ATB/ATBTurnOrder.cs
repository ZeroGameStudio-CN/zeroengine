using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.RPG.TurnBased.Variants
{
    /// <summary>
    /// ATB (Active Time Battle) 行动顺序计算器 - 最终幻想风格
    /// 每个单位有一个 ATB 槽，根据速度填充，满了可以行动
    /// </summary>
    public class ATBTurnOrder : ITurnOrderCalculator
    {
        #region 配置

        /// <summary>
        /// ATB 槽最大值
        /// </summary>
        public float MaxATB { get; set; } = 100f;

        /// <summary>
        /// 基础填充速度倍率
        /// </summary>
        public float BaseChargeRate { get; set; } = 1f;

        /// <summary>
        /// 速度转换为填充速度的系数
        /// </summary>
        public float SpeedToChargeMultiplier { get; set; } = 0.1f;

        /// <summary>
        /// 行动后 ATB 槽重置到的值 (0 = 完全重置)
        /// </summary>
        public float ResetValue { get; set; } = 0f;

        /// <summary>
        /// 是否在等待玩家输入时暂停 ATB
        /// </summary>
        public bool PauseOnPlayerInput { get; set; } = true;

        /// <summary>
        /// ATB 模式
        /// </summary>
        public ATBMode Mode { get; set; } = ATBMode.Active;

        #endregion

        #region 状态

        /// <summary>
        /// 各单位的 ATB 值
        /// </summary>
        private readonly Dictionary<ITurnBasedCombatant, float> _atbValues = new();

        /// <summary>
        /// 准备好行动的单位队列
        /// </summary>
        private readonly List<ITurnBasedCombatant> _readyQueue = new();

        /// <summary>
        /// 是否暂停 ATB 填充
        /// </summary>
        public bool IsPaused { get; set; }

        #endregion

        /// <summary>
        /// 策略名称
        /// </summary>
        public string StrategyName => "ATB (Final Fantasy Style)";

        /// <summary>
        /// 获取单位的 ATB 值
        /// </summary>
        public float GetATBValue(ITurnBasedCombatant combatant)
        {
            return _atbValues.TryGetValue(combatant, out float value) ? value : 0f;
        }

        /// <summary>
        /// 获取单位的 ATB 百分比 (0-1)
        /// </summary>
        public float GetATBPercent(ITurnBasedCombatant combatant)
        {
            return GetATBValue(combatant) / MaxATB;
        }

        /// <summary>
        /// 检查单位是否准备好行动
        /// </summary>
        public bool IsReady(ITurnBasedCombatant combatant)
        {
            return GetATBValue(combatant) >= MaxATB;
        }

        /// <summary>
        /// 更新 ATB (每帧调用)
        /// </summary>
        /// <param name="combatants">所有参战单位</param>
        /// <param name="deltaTime">帧间隔</param>
        /// <returns>新准备好的单位列表</returns>
        public List<ITurnBasedCombatant> UpdateATB(IEnumerable<ITurnBasedCombatant> combatants, float deltaTime)
        {
            if (IsPaused) return new List<ITurnBasedCombatant>();

            var newlyReady = new List<ITurnBasedCombatant>();

            foreach (var combatant in combatants)
            {
                if (!combatant.IsAlive || !combatant.CanAct) continue;
                if (IsReady(combatant)) continue; // 已经满了的不再填充

                // 确保有初始值
                if (!_atbValues.ContainsKey(combatant))
                {
                    _atbValues[combatant] = 0f;
                }

                // 计算填充速度: 基础速度 + 单位速度 * 系数
                float chargeRate = BaseChargeRate + combatant.Speed * SpeedToChargeMultiplier;
                float previousValue = _atbValues[combatant];
                float newValue = previousValue + chargeRate * deltaTime;

                // 检查是否刚好填满
                if (newValue >= MaxATB && _atbValues[combatant] < MaxATB)
                {
                    newValue = MaxATB;
                    newlyReady.Add(combatant);

                    if (!_readyQueue.Contains(combatant))
                    {
                        _readyQueue.Add(combatant);
                    }

                    OnATBFull?.Invoke(combatant);
                    Debug.Log($"[ATBTurnOrder] {combatant.DisplayName} ATB 已满！");
                }

                _atbValues[combatant] = Mathf.Min(newValue, MaxATB);
                OnATBChanged?.Invoke(combatant, previousValue, _atbValues[combatant]);
            }

            return newlyReady;
        }

        /// <summary>
        /// 计算当前可以行动的单位顺序
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> CalculateOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            TurnBasedBattleContext context)
        {
            // 清理已死亡的单位
            _readyQueue.RemoveAll(c => !c.IsAlive);

            // 返回准备好的单位，按 ATB 值排序 (先满的先行动)
            return _readyQueue.OrderByDescending(c => GetATBValue(c));
        }

        /// <summary>
        /// 获取预览行动顺序 (模拟未来的 ATB 填充)
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> GetFutureOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            int previewTurns)
        {
            var aliveUnits = combatants.Where(c => c.IsAlive && c.CanAct).ToList();
            var result = new List<ITurnBasedCombatant>();

            // 复制当前 ATB 状态进行模拟
            var simulatedATB = new Dictionary<ITurnBasedCombatant, float>();
            foreach (var unit in aliveUnits)
            {
                simulatedATB[unit] = GetATBValue(unit);
            }

            int actionsSimulated = 0;
            int maxIterations = previewTurns * aliveUnits.Count * 10; // 防止无限循环
            int iterations = 0;

            while (actionsSimulated < previewTurns && iterations < maxIterations)
            {
                iterations++;

                // 找到最快填满 ATB 的单位
                ITurnBasedCombatant nextActor = null;
                float minTimeToFull = float.MaxValue;

                foreach (var unit in aliveUnits)
                {
                    float currentATB = simulatedATB[unit];
                    if (currentATB >= MaxATB)
                    {
                        // 已经满了
                        if (nextActor == null || currentATB > simulatedATB[nextActor])
                        {
                            nextActor = unit;
                            minTimeToFull = 0;
                        }
                    }
                    else
                    {
                        float chargeRate = BaseChargeRate + unit.Speed * SpeedToChargeMultiplier;
                        if (chargeRate > 0)
                        {
                            float timeToFull = (MaxATB - currentATB) / chargeRate;
                            if (timeToFull < minTimeToFull)
                            {
                                minTimeToFull = timeToFull;
                                nextActor = unit;
                            }
                        }
                    }
                }

                if (nextActor == null) break;

                // 推进时间
                if (minTimeToFull > 0)
                {
                    foreach (var unit in aliveUnits)
                    {
                        float chargeRate = BaseChargeRate + unit.Speed * SpeedToChargeMultiplier;
                        simulatedATB[unit] = Mathf.Min(simulatedATB[unit] + chargeRate * minTimeToFull, MaxATB);
                    }
                }

                // 记录行动
                result.Add(nextActor);
                simulatedATB[nextActor] = ResetValue;
                actionsSimulated++;
            }

            return result;
        }

        /// <summary>
        /// 单位行动后重置其 ATB
        /// </summary>
        public void OnCombatantActed(ITurnBasedCombatant combatant, TurnBasedBattleContext context)
        {
            _atbValues[combatant] = ResetValue;
            _readyQueue.Remove(combatant);

            Debug.Log($"[ATBTurnOrder] {combatant.DisplayName} 行动完毕，ATB 重置为 {ResetValue}");
        }

        /// <summary>
        /// 重置所有 ATB 状态
        /// </summary>
        public void Reset()
        {
            _atbValues.Clear();
            _readyQueue.Clear();
            IsPaused = false;

            Debug.Log("[ATBTurnOrder] ATB 系统重置");
        }

        /// <summary>
        /// 初始化单位的 ATB (可设置初始值，如根据速度给予初始填充)
        /// </summary>
        public void InitializeCombatant(ITurnBasedCombatant combatant, float initialATB = 0f)
        {
            _atbValues[combatant] = Mathf.Clamp(initialATB, 0f, MaxATB);
        }

        /// <summary>
        /// 根据速度给予初始 ATB
        /// </summary>
        public void InitializeWithSpeedBonus(IEnumerable<ITurnBasedCombatant> combatants, float speedBonusMultiplier = 0.5f)
        {
            foreach (var combatant in combatants)
            {
                float initialATB = combatant.Speed * speedBonusMultiplier;
                InitializeCombatant(combatant, initialATB);
            }
        }

        /// <summary>
        /// ATB 变化事件
        /// </summary>
        public event Action<ITurnBasedCombatant, float, float> OnATBChanged;

        /// <summary>
        /// ATB 满事件
        /// </summary>
        public event Action<ITurnBasedCombatant> OnATBFull;
    }

    /// <summary>
    /// ATB 模式
    /// </summary>
    public enum ATBMode
    {
        /// <summary>
        /// 主动模式 - ATB 持续填充，即使在选择命令时
        /// </summary>
        Active,

        /// <summary>
        /// 等待模式 - 选择命令时 ATB 暂停
        /// </summary>
        Wait,

        /// <summary>
        /// 半主动 - 打开菜单时暂停，选择目标时继续
        /// </summary>
        SemiActive
    }
}
