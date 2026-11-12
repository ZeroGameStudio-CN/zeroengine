using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.RPG.TurnBased
{
    /// <summary>
    /// 战斗行动数据 - 描述一次战斗行动的所有信息
    /// </summary>
    public class BattleAction
    {
        /// <summary>
        /// 行动执行者
        /// </summary>
        public ITurnBasedCombatant Actor { get; set; }

        /// <summary>
        /// 行动类型
        /// </summary>
        public BattleActionType ActionType { get; set; }

        /// <summary>
        /// 目标列表
        /// </summary>
        public List<ITurnBasedCombatant> Targets { get; set; } = new();

        /// <summary>
        /// 技能ID (如果是技能行动)
        /// </summary>
        public string SkillId { get; set; }

        /// <summary>
        /// 物品ID (如果是物品行动)
        /// </summary>
        public string ItemId { get; set; }

        /// <summary>
        /// BP 强化等级 (0-5, 八方旅人特色)
        /// </summary>
        public int BoostLevel { get; set; }

        /// <summary>
        /// 自定义数据字典 (用于键值对扩展)
        /// </summary>
        public Dictionary<string, object> CustomData { get; set; } = new();

        /// <summary>
        /// 扩展数据对象 (用于类型化扩展，如 APActionData, CTBActionData)
        /// </summary>
        public object ExtendedData { get; set; }

        /// <summary>
        /// 行动是否已执行
        /// </summary>
        public bool IsExecuted { get; set; }

        /// <summary>
        /// 行动是否成功
        /// </summary>
        public bool IsSuccessful { get; set; }

        public string DisplayName => ActionType switch
        {
            BattleActionType.Attack => "Attack",
            BattleActionType.Skill => "Skill",
            BattleActionType.Item => "Item",
            BattleActionType.Defend => "Defend",
            BattleActionType.Escape => "Escape",
            _ => "None"
        };

        public bool RequiresTarget => ActionType == BattleActionType.Attack
            || ActionType == BattleActionType.Skill
            || ActionType == BattleActionType.Item;

        public bool IsTargetedAction => RequiresTarget && Targets != null && Targets.Count > 0;

        /// <summary>
        /// 创建普通攻击行动
        /// </summary>
        public static BattleAction Attack(ITurnBasedCombatant actor, ITurnBasedCombatant target, int boostLevel = 0)
        {
            return new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.Attack,
                Targets = new List<ITurnBasedCombatant> { target },
                BoostLevel = boostLevel
            };
        }

        /// <summary>
        /// 创建技能行动
        /// </summary>
        public static BattleAction Skill(ITurnBasedCombatant actor, string skillId,
            List<ITurnBasedCombatant> targets, int boostLevel = 0)
        {
            return new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.Skill,
                SkillId = skillId,
                Targets = targets ?? new List<ITurnBasedCombatant>(),
                BoostLevel = boostLevel
            };
        }

        /// <summary>
        /// 创建物品行动
        /// </summary>
        public static BattleAction Item(ITurnBasedCombatant actor, string itemId,
            ITurnBasedCombatant target = null)
        {
            var action = new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.Item,
                ItemId = itemId
            };
            if (target != null)
            {
                action.Targets.Add(target);
            }
            return action;
        }

        /// <summary>
        /// 创建防御行动
        /// </summary>
        public static BattleAction Defend(ITurnBasedCombatant actor)
        {
            return new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.Defend,
                Targets = new List<ITurnBasedCombatant> { actor }
            };
        }

        /// <summary>
        /// 创建逃跑行动
        /// </summary>
        public static BattleAction Escape(ITurnBasedCombatant actor)
        {
            return new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.Escape
            };
        }

        /// <summary>
        /// 创建跳过行动
        /// </summary>
        public static BattleAction Skip(ITurnBasedCombatant actor)
        {
            return new BattleAction
            {
                Actor = actor,
                ActionType = BattleActionType.None
            };
        }
    }
}
