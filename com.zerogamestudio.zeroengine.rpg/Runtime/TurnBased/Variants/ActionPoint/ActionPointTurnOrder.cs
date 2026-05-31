using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.RPG.TurnBased.Variants
{
    /// <summary>
    /// 行动点制 (Action Point) 行动顺序计算器
    /// 每个单位有行动点，不同行动消耗不同点数，可多次行动
    /// </summary>
    public class ActionPointTurnOrder : ITurnOrderCalculator
    {
        #region 配置

        /// <summary>
        /// 默认最大行动点
        /// </summary>
        public int DefaultMaxAP { get; set; } = 100;

        /// <summary>
        /// 每点速度增加的行动点
        /// </summary>
        public float SpeedToAPBonus { get; set; } = 1f;

        /// <summary>
        /// 是否在回合开始时完全恢复 AP
        /// </summary>
        public bool FullRestoreOnTurnStart { get; set; } = true;

        /// <summary>
        /// 回合开始恢复的 AP 百分比 (当 FullRestoreOnTurnStart = false 时使用)
        /// </summary>
        public float TurnStartAPRecoveryPercent { get; set; } = 0.5f;

        /// <summary>
        /// 默认行动 AP 消耗
        /// </summary>
        public Dictionary<BattleActionType, int> DefaultActionCosts { get; } = new()
        {
            { BattleActionType.Attack, 30 },
            { BattleActionType.Skill, 50 },
            { BattleActionType.Item, 20 },
            { BattleActionType.Defend, 10 },
            { BattleActionType.Escape, 100 },
            { BattleActionType.None, 0 }
        };

        #endregion

        #region 状态

        /// <summary>
        /// 各单位的当前 AP
        /// </summary>
        private readonly Dictionary<ITurnBasedCombatant, int> _currentAP = new();

        /// <summary>
        /// 各单位的最大 AP
        /// </summary>
        private readonly Dictionary<ITurnBasedCombatant, int> _maxAP = new();

        /// <summary>
        /// 当前回合已行动的单位列表 (用于控制回合流程)
        /// </summary>
        private readonly HashSet<ITurnBasedCombatant> _actedThisTurn = new();

        /// <summary>
        /// 行动顺序列表
        /// </summary>
        private readonly List<ITurnBasedCombatant> _turnOrder = new();

        #endregion

        /// <summary>
        /// 策略名称
        /// </summary>
        public string StrategyName => "Action Point System";

        /// <summary>
        /// 获取单位的当前 AP
        /// </summary>
        public int GetCurrentAP(ITurnBasedCombatant combatant)
        {
            return _currentAP.TryGetValue(combatant, out int ap) ? ap : 0;
        }

        /// <summary>
        /// 获取单位的最大 AP
        /// </summary>
        public int GetMaxAP(ITurnBasedCombatant combatant)
        {
            if (_maxAP.TryGetValue(combatant, out int maxAP))
            {
                return maxAP;
            }

            // 计算默认最大 AP (基础 + 速度加成)
            return DefaultMaxAP + Mathf.RoundToInt(combatant.Speed * SpeedToAPBonus);
        }

        /// <summary>
        /// 获取 AP 百分比 (0-1)
        /// </summary>
        public float GetAPPercent(ITurnBasedCombatant combatant)
        {
            int maxAP = GetMaxAP(combatant);
            return maxAP > 0 ? (float)GetCurrentAP(combatant) / maxAP : 0f;
        }

        /// <summary>
        /// 获取行动消耗的 AP
        /// </summary>
        public int GetActionCost(BattleActionType actionType, int customCost = -1)
        {
            if (customCost >= 0) return customCost;
            return DefaultActionCosts.TryGetValue(actionType, out int cost) ? cost : 30;
        }

        /// <summary>
        /// 检查单位是否有足够 AP 执行行动
        /// </summary>
        public bool CanAffordAction(ITurnBasedCombatant combatant, BattleActionType actionType, int customCost = -1)
        {
            int cost = GetActionCost(actionType, customCost);
            return GetCurrentAP(combatant) >= cost;
        }

        /// <summary>
        /// 消耗 AP
        /// </summary>
        /// <param name="combatant">单位</param>
        /// <param name="amount">消耗量</param>
        /// <returns>是否成功消耗</returns>
        public bool ConsumeAP(ITurnBasedCombatant combatant, int amount)
        {
            int currentAP = GetCurrentAP(combatant);
            if (currentAP < amount)
            {
                Debug.LogWarning($"[ActionPointTurnOrder] {combatant.DisplayName} AP 不足: 需要 {amount}，当前 {currentAP}");
                return false;
            }

            _currentAP[combatant] = currentAP - amount;
            Debug.Log($"[ActionPointTurnOrder] {combatant.DisplayName} 消耗 {amount} AP: {currentAP} -> {_currentAP[combatant]}");

            OnAPChanged?.Invoke(combatant, currentAP, _currentAP[combatant]);
            return true;
        }

        /// <summary>
        /// 恢复 AP
        /// </summary>
        public void RestoreAP(ITurnBasedCombatant combatant, int amount)
        {
            int currentAP = GetCurrentAP(combatant);
            int maxAP = GetMaxAP(combatant);
            int newAP = Mathf.Min(currentAP + amount, maxAP);

            if (newAP != currentAP)
            {
                _currentAP[combatant] = newAP;
                OnAPChanged?.Invoke(combatant, currentAP, newAP);
            }
        }

        /// <summary>
        /// 计算当前回合的行动顺序
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> CalculateOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            TurnBasedBattleContext context)
        {
            var aliveUnits = combatants.Where(c => c.IsAlive && c.CanAct).ToList();

            // 初始化未注册的单位
            foreach (var unit in aliveUnits)
            {
                if (!_currentAP.ContainsKey(unit))
                {
                    InitializeCombatant(unit);
                }
            }

            // 如果是新回合，重新计算行动顺序
            if (_turnOrder.Count == 0 || context.TurnCount > 0 && _actedThisTurn.Count == 0)
            {
                CalculateTurnOrder(aliveUnits);
            }

            // 返回还有 AP 的单位，按行动顺序排列
            return _turnOrder.Where(c => c.IsAlive && c.CanAct && GetCurrentAP(c) > 0);
        }

        /// <summary>
        /// 计算回合内的行动顺序 (按速度排序)
        /// </summary>
        private void CalculateTurnOrder(List<ITurnBasedCombatant> aliveUnits)
        {
            _turnOrder.Clear();
            _turnOrder.AddRange(aliveUnits.OrderByDescending(c => c.Speed));
            _actedThisTurn.Clear();
        }

        /// <summary>
        /// 获取预览行动顺序
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> GetFutureOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            int previewTurns)
        {
            var aliveUnits = combatants.Where(c => c.IsAlive && c.CanAct).ToList();
            var result = new List<ITurnBasedCombatant>();

            // 模拟 AP 状态
            var simulatedAP = new Dictionary<ITurnBasedCombatant, int>();
            foreach (var unit in aliveUnits)
            {
                simulatedAP[unit] = GetCurrentAP(unit);
            }

            // 行动顺序 (按速度)
            var orderedUnits = aliveUnits.OrderByDescending(c => c.Speed).ToList();

            int actionsSimulated = 0;
            int turnsPassed = 0;

            while (actionsSimulated < previewTurns && turnsPassed < previewTurns)
            {
                bool anyActed = false;

                foreach (var unit in orderedUnits)
                {
                    // 假设每次行动消耗普通攻击的 AP
                    int attackCost = GetActionCost(BattleActionType.Attack);

                    while (simulatedAP[unit] >= attackCost && actionsSimulated < previewTurns)
                    {
                        result.Add(unit);
                        simulatedAP[unit] -= attackCost;
                        actionsSimulated++;
                        anyActed = true;
                    }
                }

                // 回合结束，恢复 AP
                if (!anyActed || orderedUnits.All(u => simulatedAP[u] < GetActionCost(BattleActionType.Attack)))
                {
                    foreach (var unit in orderedUnits)
                    {
                        if (FullRestoreOnTurnStart)
                        {
                            simulatedAP[unit] = GetMaxAP(unit);
                        }
                        else
                        {
                            int maxAP = GetMaxAP(unit);
                            int recovery = Mathf.RoundToInt(maxAP * TurnStartAPRecoveryPercent);
                            simulatedAP[unit] = Mathf.Min(simulatedAP[unit] + recovery, maxAP);
                        }
                    }
                    turnsPassed++;
                }
            }

            return result;
        }

        /// <summary>
        /// 单位行动后更新状态
        /// </summary>
        public void OnCombatantActed(ITurnBasedCombatant combatant, TurnBasedBattleContext context)
        {
            var action = context.LastAction;
            int apCost = GetActionCost(action?.ActionType ?? BattleActionType.Attack);

            // 检查是否有自定义 AP 消耗
            if (action?.ExtendedData is APActionData apData)
            {
                apCost = apData.APCost;
            }

            ConsumeAP(combatant, apCost);
            _actedThisTurn.Add(combatant);

            // 检查是否还能继续行动
            bool canContinue = GetCurrentAP(combatant) >= GetMinActionCost();

            Debug.Log($"[ActionPointTurnOrder] {combatant.DisplayName} 行动完毕，剩余 AP: {GetCurrentAP(combatant)}，可继续: {canContinue}");
        }

        /// <summary>
        /// 获取最小行动消耗
        /// </summary>
        private int GetMinActionCost()
        {
            return DefaultActionCosts.Values.Where(c => c > 0).DefaultIfEmpty(10).Min();
        }

        /// <summary>
        /// 重置状态
        /// </summary>
        public void Reset()
        {
            _currentAP.Clear();
            _maxAP.Clear();
            _actedThisTurn.Clear();
            _turnOrder.Clear();

            Debug.Log("[ActionPointTurnOrder] AP 系统重置");
        }

        /// <summary>
        /// 初始化单位
        /// </summary>
        public void InitializeCombatant(ITurnBasedCombatant combatant, int? customMaxAP = null)
        {
            int maxAP = customMaxAP ?? (DefaultMaxAP + Mathf.RoundToInt(combatant.Speed * SpeedToAPBonus));
            _maxAP[combatant] = maxAP;
            _currentAP[combatant] = maxAP;

            Debug.Log($"[ActionPointTurnOrder] 初始化 {combatant.DisplayName}: MaxAP = {maxAP}");
        }

        /// <summary>
        /// 回合开始时恢复 AP
        /// </summary>
        public void OnTurnStart(IEnumerable<ITurnBasedCombatant> combatants)
        {
            _actedThisTurn.Clear();

            foreach (var combatant in combatants.Where(c => c.IsAlive))
            {
                int maxAP = GetMaxAP(combatant);
                int currentAP = GetCurrentAP(combatant);
                int newAP;

                if (FullRestoreOnTurnStart)
                {
                    newAP = maxAP;
                }
                else
                {
                    int recovery = Mathf.RoundToInt(maxAP * TurnStartAPRecoveryPercent);
                    newAP = Mathf.Min(currentAP + recovery, maxAP);
                }

                if (newAP != currentAP)
                {
                    _currentAP[combatant] = newAP;
                    OnAPChanged?.Invoke(combatant, currentAP, newAP);
                }
            }

            // 重新计算行动顺序
            CalculateTurnOrder(combatants.Where(c => c.IsAlive && c.CanAct).ToList());
        }

        /// <summary>
        /// 检查单位本回合是否还能行动
        /// </summary>
        public bool CanActThisTurn(ITurnBasedCombatant combatant)
        {
            return combatant.IsAlive && combatant.CanAct && GetCurrentAP(combatant) >= GetMinActionCost();
        }

        /// <summary>
        /// 获取单位可执行的行动类型
        /// </summary>
        public List<BattleActionType> GetAffordableActions(ITurnBasedCombatant combatant)
        {
            int currentAP = GetCurrentAP(combatant);
            return DefaultActionCosts
                .Where(kvp => kvp.Value <= currentAP)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// AP 变化事件
        /// </summary>
        public event Action<ITurnBasedCombatant, int, int> OnAPChanged;
    }

    /// <summary>
    /// AP 行动附加数据 - 用于自定义 AP 消耗
    /// </summary>
    public class APActionData
    {
        /// <summary>
        /// 自定义 AP 消耗
        /// </summary>
        public int APCost { get; set; }

        /// <summary>
        /// 创建自定义 AP 消耗数据
        /// </summary>
        public static APActionData Create(int cost) => new() { APCost = cost };

        /// <summary>
        /// 免费行动 (0 AP)
        /// </summary>
        public static APActionData Free() => new() { APCost = 0 };
    }
}
