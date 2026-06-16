using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.RPG.TurnBased.Variants
{
    /// <summary>
    /// CTB (Conditional Turn-Based) 行动顺序计算器 - FFX 风格
    /// 使用时间轴系统，每个行动有恢复时间，决定下次行动的顺序
    /// </summary>
    public class CTBTurnOrder : ITurnOrderCalculator
    {
        #region 配置

        /// <summary>
        /// 基础行动延迟 (速度为 0 时的延迟)
        /// </summary>
        public int BaseActionDelay { get; set; } = 100;

        /// <summary>
        /// 速度对延迟的减少系数 (延迟 = BaseDelay - Speed * SpeedFactor)
        /// </summary>
        public float SpeedDelayReduction { get; set; } = 0.5f;

        /// <summary>
        /// 最小行动延迟
        /// </summary>
        public int MinActionDelay { get; set; } = 10;

        /// <summary>
        /// 最大行动延迟
        /// </summary>
        public int MaxActionDelay { get; set; } = 200;

        /// <summary>
        /// 时间轴预览长度
        /// </summary>
        public int TimelinePreviewLength { get; set; } = 12;

        #endregion

        #region 状态

        /// <summary>
        /// 各单位在时间轴上的位置 (tick)
        /// </summary>
        private readonly Dictionary<ITurnBasedCombatant, int> _timeline = new();

        /// <summary>
        /// 当前时间轴 tick
        /// </summary>
        private int _currentTick = 0;

        #endregion

        /// <summary>
        /// 策略名称
        /// </summary>
        public string StrategyName => "CTB (FFX Style)";

        /// <summary>
        /// 获取单位在时间轴上的位置
        /// </summary>
        public int GetTimelinePosition(ITurnBasedCombatant combatant)
        {
            return _timeline.TryGetValue(combatant, out int pos) ? pos : 0;
        }

        /// <summary>
        /// 获取单位距离下次行动的 tick 数
        /// </summary>
        public int GetTicksUntilAction(ITurnBasedCombatant combatant)
        {
            int position = GetTimelinePosition(combatant);
            return Math.Max(0, position - _currentTick);
        }

        /// <summary>
        /// 计算行动延迟
        /// </summary>
        /// <param name="combatant">行动单位</param>
        /// <param name="actionType">行动类型</param>
        /// <param name="actionDelayModifier">行动延迟修正 (某些技能可能更慢或更快)</param>
        /// <returns>计算后的延迟值</returns>
        public int CalculateActionDelay(ITurnBasedCombatant combatant, BattleActionType actionType, float actionDelayModifier = 1f)
        {
            // 基础延迟
            float delay = BaseActionDelay;

            // 速度减少延迟
            delay -= combatant.Speed * SpeedDelayReduction;

            // 行动类型修正
            delay *= GetActionTypeDelayMultiplier(actionType);

            // 自定义修正
            delay *= actionDelayModifier;

            return Mathf.Clamp(Mathf.RoundToInt(delay), MinActionDelay, MaxActionDelay);
        }

        /// <summary>
        /// 获取行动类型的延迟倍率
        /// </summary>
        private float GetActionTypeDelayMultiplier(BattleActionType actionType)
        {
            return actionType switch
            {
                BattleActionType.Attack => 1.0f,
                BattleActionType.Skill => 1.2f,      // 技能稍慢
                BattleActionType.Item => 0.8f,      // 道具较快
                BattleActionType.Defend => 0.5f,    // 防御很快
                BattleActionType.Escape => 1.5f,    // 逃跑很慢
                BattleActionType.None => 0.3f,      // 跳过最快
                _ => 1.0f
            };
        }

        /// <summary>
        /// 计算当前应该行动的单位顺序
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> CalculateOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            TurnBasedBattleContext context)
        {
            var aliveUnits = combatants.Where(c => c.IsAlive && c.CanAct).ToList();

            // 初始化未在时间轴上的单位
            foreach (var unit in aliveUnits)
            {
                if (!_timeline.ContainsKey(unit))
                {
                    // 根据速度给予初始位置
                    int initialDelay = CalculateActionDelay(unit, BattleActionType.None, 0.5f);
                    _timeline[unit] = _currentTick + initialDelay;
                }
            }

            // 按时间轴位置排序，位置小的先行动
            return aliveUnits
                .OrderBy(c => GetTimelinePosition(c))
                .ThenByDescending(c => c.Speed); // 同时刻时，速度快的优先
        }

        /// <summary>
        /// 获取预览行动顺序 (时间轴显示)
        /// </summary>
        public IEnumerable<ITurnBasedCombatant> GetFutureOrder(
            IEnumerable<ITurnBasedCombatant> combatants,
            int previewTurns)
        {
            var aliveUnits = combatants.Where(c => c.IsAlive && c.CanAct).ToList();
            var result = new List<ITurnBasedCombatant>();

            // 复制时间轴进行模拟
            var simulatedTimeline = new Dictionary<ITurnBasedCombatant, int>();
            foreach (var unit in aliveUnits)
            {
                simulatedTimeline[unit] = GetTimelinePosition(unit);
            }

            int simulatedTick = _currentTick;
            int actionsSimulated = 0;
            int maxIterations = previewTurns * 2; // 防止无限循环

            while (actionsSimulated < previewTurns && actionsSimulated < maxIterations)
            {
                // 找到下一个行动的单位
                ITurnBasedCombatant nextActor = null;
                int minTick = int.MaxValue;

                foreach (var unit in aliveUnits)
                {
                    int tick = simulatedTimeline[unit];
                    if (tick < minTick || (tick == minTick && nextActor != null && unit.Speed > nextActor.Speed))
                    {
                        minTick = tick;
                        nextActor = unit;
                    }
                }

                if (nextActor == null) break;

                result.Add(nextActor);

                // 模拟行动后的延迟 (假设使用普通攻击)
                int delay = CalculateActionDelay(nextActor, BattleActionType.Attack);
                simulatedTimeline[nextActor] = minTick + delay;
                simulatedTick = minTick;
                actionsSimulated++;
            }

            return result;
        }

        /// <summary>
        /// 单位行动后更新时间轴
        /// </summary>
        public void OnCombatantActed(ITurnBasedCombatant combatant, TurnBasedBattleContext context)
        {
            // 获取行动类型和延迟修正
            var action = context.LastAction;
            float delayModifier = 1f;

            if (action != null && action.ExtendedData is CTBActionData ctbData)
            {
                delayModifier = ctbData.DelayModifier;
            }

            int delay = CalculateActionDelay(combatant, action?.ActionType ?? BattleActionType.Attack, delayModifier);

            // 更新时间轴位置
            _timeline[combatant] = _currentTick + delay;
            OnTimelineChanged?.Invoke(combatant, _timeline[combatant]);

            // 推进当前 tick 到最近的行动点
            UpdateCurrentTick();

            Debug.Log($"[CTBTurnOrder] {combatant.DisplayName} 行动完毕，延迟 {delay} tick，下次行动在 tick {_timeline[combatant]}");
        }

        /// <summary>
        /// 更新当前 tick 到最近的行动点
        /// </summary>
        private void UpdateCurrentTick()
        {
            if (_timeline.Count == 0) return;

            int minTick = _timeline.Values.Where(t => t >= _currentTick).DefaultIfEmpty(_currentTick).Min();
            _currentTick = minTick;
        }

        /// <summary>
        /// 重置时间轴
        /// </summary>
        public void Reset()
        {
            _timeline.Clear();
            _currentTick = 0;

            Debug.Log("[CTBTurnOrder] CTB 时间轴重置");
        }

        /// <summary>
        /// 初始化单位在时间轴上的位置
        /// </summary>
        public void InitializeCombatant(ITurnBasedCombatant combatant, int initialTick = -1)
        {
            if (initialTick < 0)
            {
                // 根据速度计算初始位置
                initialTick = CalculateActionDelay(combatant, BattleActionType.None, 0.5f);
            }

            _timeline[combatant] = _currentTick + initialTick;
            OnTimelineChanged?.Invoke(combatant, _timeline[combatant]);
        }

        /// <summary>
        /// 获取完整的时间轴预览
        /// </summary>
        /// <returns>按时间顺序排列的 (单位, tick) 列表</returns>
        public List<(ITurnBasedCombatant combatant, int tick)> GetTimelinePreview()
        {
            return _timeline
                .Where(kvp => kvp.Key.IsAlive)
                .OrderBy(kvp => kvp.Value)
                .ThenByDescending(kvp => kvp.Key.Speed)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToList();
        }

        /// <summary>
        /// 时间轴变化事件
        /// </summary>
        public event Action<ITurnBasedCombatant, int> OnTimelineChanged;
    }

    /// <summary>
    /// CTB 行动附加数据 - 用于自定义延迟修正
    /// </summary>
    public class CTBActionData
    {
        /// <summary>
        /// 延迟修正倍率 (1.0 = 正常, 0.5 = 快速, 2.0 = 缓慢)
        /// </summary>
        public float DelayModifier { get; set; } = 1f;

        /// <summary>
        /// 创建快速行动数据
        /// </summary>
        public static CTBActionData Fast(float modifier = 0.5f) => new() { DelayModifier = modifier };

        /// <summary>
        /// 创建慢速行动数据
        /// </summary>
        public static CTBActionData Slow(float modifier = 2f) => new() { DelayModifier = modifier };

        /// <summary>
        /// 创建正常行动数据
        /// </summary>
        public static CTBActionData Normal() => new() { DelayModifier = 1f };
    }
}
