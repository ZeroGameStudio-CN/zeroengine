using System;
using UnityEngine;
using ZeroEngine.AutoBattle.Grid;
using ZeroEngine.AutoBattle.Battle;

namespace ZeroEngine.AutoBattle.Skill
{
    /// <summary>
    /// 技能数据基类
    /// </summary>
    [Serializable]
    public abstract class SkillData
    {
        /// <summary>
        /// 技能唯一ID
        /// </summary>
        public string SkillId { get; protected set; }

        /// <summary>
        /// 技能名称
        /// </summary>
        public string SkillName { get; protected set; }

        /// <summary>
        /// 技能描述
        /// </summary>
        public string Description { get; protected set; }

        /// <summary>
        /// 技能图标
        /// </summary>
        public Sprite Icon { get; protected set; }

        /// <summary>
        /// 冷却时间（秒）
        /// </summary>
        public float Cooldown { get; protected set; } = 5f;

        /// <summary>
        /// 技能范围
        /// </summary>
        public int Range { get; protected set; } = 1;

        /// <summary>
        /// 技能类型
        /// </summary>
        public SkillType Type { get; protected set; } = SkillType.Damage;

        /// <summary>
        /// 目标类型
        /// </summary>
        public SkillTargetType TargetType { get; protected set; } = SkillTargetType.SingleEnemy;

        /// <summary>
        /// 基础伤害/治疗量
        /// </summary>
        public float BaseValue { get; protected set; } = 100f;

        /// <summary>
        /// 攻击力加成系数
        /// </summary>
        public float AttackRatio { get; protected set; } = 1f;

        /// <summary>
        /// 检查技能是否可以对目标使用
        /// </summary>
        public virtual bool CanUse(IBattleUnit owner, IBattleUnit target)
        {
            if (owner == null || !owner.IsAlive)
                return false;

            // 检查目标类型
            switch (TargetType)
            {
                case SkillTargetType.Self:
                    return true;

                case SkillTargetType.SingleEnemy:
                case SkillTargetType.AllEnemies:
                    return target != null && target.IsAlive && target.Team != owner.Team;

                case SkillTargetType.SingleAlly:
                case SkillTargetType.AllAllies:
                    return target != null && target.IsAlive && target.Team == owner.Team;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Checks target validity and configured range in the active battle.
        /// </summary>
        public virtual bool CanUse(
            IBattleUnit owner,
            IBattleUnit target,
            AutoBattleManager battleManager)
        {
            if (!CanUse(owner, target))
            {
                return false;
            }

            if (TargetType == SkillTargetType.Self || TargetType == SkillTargetType.AllAllies)
            {
                return true;
            }

            return battleManager != null
                && target != null
                && battleManager.GetDistance(owner, target) <= Range;
        }

        /// <summary>
        /// 执行技能
        /// </summary>
        public abstract void Execute(IBattleUnit owner, IBattleUnit target, AutoBattleManager battleManager);

        /// <summary>
        /// 计算实际伤害/治疗值
        /// </summary>
        protected float CalculateValue(IBattleUnit owner)
        {
            float attack = 0f;
            if (owner is BattleUnitBase unit)
            {
                attack = unit.Attack;
            }
            return BaseValue + attack * AttackRatio;
        }
    }

    /// <summary>
    /// 技能类型
    /// </summary>
    public enum SkillType
    {
        /// <summary>
        /// 伤害技能
        /// </summary>
        Damage,

        /// <summary>
        /// 治疗技能
        /// </summary>
        Heal,

        /// <summary>
        /// 增益技能
        /// </summary>
        Buff,

        /// <summary>
        /// 减益技能
        /// </summary>
        Debuff,

        /// <summary>
        /// 控制技能
        /// </summary>
        Control,

        /// <summary>
        /// 召唤技能
        /// </summary>
        Summon
    }

    /// <summary>
    /// 技能目标类型
    /// </summary>
    public enum SkillTargetType
    {
        /// <summary>
        /// 自身
        /// </summary>
        Self,

        /// <summary>
        /// 单个敌人
        /// </summary>
        SingleEnemy,

        /// <summary>
        /// 所有敌人
        /// </summary>
        AllEnemies,

        /// <summary>
        /// 单个友军
        /// </summary>
        SingleAlly,

        /// <summary>
        /// 所有友军
        /// </summary>
        AllAllies,

        /// <summary>
        /// 区域（AOE）
        /// </summary>
        Area
    }
}
