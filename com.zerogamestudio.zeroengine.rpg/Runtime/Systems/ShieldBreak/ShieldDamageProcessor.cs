using UnityEngine;
using ZeroEngine.Combat;
using ZeroEngine.Utils;

namespace ZeroEngine.RPG.Systems
{
    /// <summary>
    /// 护盾伤害处理器 - 根据破盾状态和弱点命中增加伤害
    /// </summary>
    public class ShieldDamageProcessor : IDamageProcessor
    {
        /// <summary>
        /// 处理器优先级 (在 Boost 处理器之后)
        /// </summary>
        public int Priority => 200;

        /// <summary>
        /// 处理伤害数据
        /// </summary>
        public DamageData ProcessDamage(DamageData damage, ICombatant target, DamageCalculationContext context)
        {
            // 获取目标的 ShieldComponent
            var shieldable = GetShieldable(target);
            if (shieldable == null)
            {
                return damage;
            }

            // 获取攻击的弱点类型
            var shieldData = GetShieldData(damage);
            WeaknessType attackType = shieldData?.AttackType ?? WeaknessType.None;

            float multiplier = 1f;
            bool isWeakness = false;
            bool isBroken = shieldable.IsBroken;

            // 检查弱点命中
            if (attackType != WeaknessType.None)
            {
                isWeakness = shieldable.CheckWeakness(attackType);
                if (isWeakness)
                {
                    multiplier *= ShieldConstants.WEAKNESS_DAMAGE_MULTIPLIER;
                    DebugUtils.Log($"[ShieldDamageProcessor] 命中弱点 {attackType.GetDisplayName()}! x{ShieldConstants.WEAKNESS_DAMAGE_MULTIPLIER:F2}");
                }
            }

            // 检查破盾状态
            if (isBroken)
            {
                multiplier *= ShieldConstants.BREAK_DAMAGE_MULTIPLIER;
                DebugUtils.Log($"[ShieldDamageProcessor] 目标处于破盾状态! x{ShieldConstants.BREAK_DAMAGE_MULTIPLIER:F2}");
            }

            // 应用伤害加成
            if (multiplier > 1f)
            {
                float newDamage = damage.BaseDamage * multiplier;
                DebugUtils.Log($"[ShieldDamageProcessor] 伤害加成: {damage.BaseDamage:F1} x {multiplier:F2} = {newDamage:F1}");

                // 更新 ShieldData 记录
                if (shieldData != null)
                {
                    shieldData.IsWeaknessHit = isWeakness;
                    shieldData.IsTargetBroken = isBroken;
                    shieldData.FinalMultiplier = multiplier;
                }

                return damage.WithBaseDamage(newDamage);
            }

            return damage;
        }

        /// <summary>
        /// 从目标获取 IShieldable 组件
        /// </summary>
        private IShieldable GetShieldable(ICombatant target)
        {
            if (target == null) return null;

            // 尝试从 GameObject 获取 ShieldComponent
            if (target.GameObject != null)
            {
                return target.GameObject.GetComponent<IShieldable>();
            }

            // 如果目标本身实现了 IShieldable
            return target as IShieldable;
        }

        /// <summary>
        /// 从伤害数据中获取 Shield 信息
        /// </summary>
        private ShieldDamageData GetShieldData(DamageData damage)
        {
            // 优先检查 ShieldDamageData
            if (damage.CustomData is ShieldDamageData shieldData)
            {
                return shieldData;
            }

            // 如果是 CombinedDamageData，从中提取
            if (damage.CustomData is CombinedDamageData combined)
            {
                return combined.ShieldData;
            }

            return null;
        }
    }

    /// <summary>
    /// Shield 伤害附加数据 - 存储在 DamageData.CustomData 中
    /// </summary>
    public class ShieldDamageData
    {
        /// <summary>
        /// 攻击类型 (用于弱点检测)
        /// </summary>
        public WeaknessType AttackType { get; set; }

        /// <summary>
        /// 护盾伤害量 (命中弱点时)
        /// </summary>
        public int ShieldDamage { get; set; } = ShieldConstants.SHIELD_DAMAGE_PER_WEAKNESS_HIT;

        /// <summary>
        /// 是否命中弱点 (处理后设置)
        /// </summary>
        public bool IsWeaknessHit { get; set; }

        /// <summary>
        /// 目标是否处于破盾状态 (处理后设置)
        /// </summary>
        public bool IsTargetBroken { get; set; }

        /// <summary>
        /// 最终伤害倍率 (处理后设置)
        /// </summary>
        public float FinalMultiplier { get; set; } = 1f;

        /// <summary>
        /// 创建 Shield 伤害数据
        /// </summary>
        public static ShieldDamageData Create(WeaknessType attackType, int shieldDamage = ShieldConstants.SHIELD_DAMAGE_PER_WEAKNESS_HIT)
        {
            return new ShieldDamageData
            {
                AttackType = attackType,
                ShieldDamage = shieldDamage
            };
        }
    }

    /// <summary>
    /// 组合伤害数据 - 同时包含 Boost 和 Shield 数据
    /// </summary>
    public class CombinedDamageData
    {
        /// <summary>
        /// Boost 数据
        /// </summary>
        public BoostDamageData BoostData { get; set; }

        /// <summary>
        /// Shield 数据
        /// </summary>
        public ShieldDamageData ShieldData { get; set; }

        /// <summary>
        /// 创建组合伤害数据
        /// </summary>
        public static CombinedDamageData Create(int boostLevel, WeaknessType attackType)
        {
            return new CombinedDamageData
            {
                BoostData = boostLevel > 0 ? BoostDamageData.Create(boostLevel) : null,
                ShieldData = attackType != WeaknessType.None ? ShieldDamageData.Create(attackType) : null
            };
        }
    }

    /// <summary>
    /// DamageData 扩展方法 - 用于添加 Shield 信息
    /// </summary>
    public static class ShieldDamageExtensions
    {
        /// <summary>
        /// 为伤害数据添加攻击类型 (用于弱点检测)
        /// </summary>
        public static DamageData WithAttackType(this DamageData damage, WeaknessType attackType)
        {
            if (attackType == WeaknessType.None) return damage;

            // 检查是否已有 BoostDamageData
            if (damage.CustomData is BoostDamageData boostData)
            {
                // 转换为组合数据
                damage.CustomData = new CombinedDamageData
                {
                    BoostData = boostData,
                    ShieldData = ShieldDamageData.Create(attackType)
                };
            }
            else if (damage.CustomData is CombinedDamageData combined)
            {
                // 更新已有的组合数据
                combined.ShieldData = ShieldDamageData.Create(attackType);
            }
            else
            {
                // 直接设置 ShieldDamageData
                damage.CustomData = ShieldDamageData.Create(attackType);
            }

            return damage;
        }

        /// <summary>
        /// 同时设置 Boost 等级和攻击类型
        /// </summary>
        public static DamageData WithBoostAndAttackType(this DamageData damage, int boostLevel, WeaknessType attackType)
        {
            if (boostLevel <= 0 && attackType == WeaknessType.None)
            {
                return damage;
            }

            damage.CustomData = CombinedDamageData.Create(boostLevel, attackType);
            return damage;
        }

        /// <summary>
        /// 获取伤害数据中的攻击类型
        /// </summary>
        public static WeaknessType GetAttackType(this DamageData damage)
        {
            if (damage.CustomData is ShieldDamageData shieldData)
            {
                return shieldData.AttackType;
            }
            if (damage.CustomData is CombinedDamageData combined && combined.ShieldData != null)
            {
                return combined.ShieldData.AttackType;
            }
            return WeaknessType.None;
        }

        /// <summary>
        /// 检查伤害是否命中了弱点
        /// </summary>
        public static bool WasWeaknessHit(this DamageData damage)
        {
            if (damage.CustomData is ShieldDamageData shieldData)
            {
                return shieldData.IsWeaknessHit;
            }
            if (damage.CustomData is CombinedDamageData combined && combined.ShieldData != null)
            {
                return combined.ShieldData.IsWeaknessHit;
            }
            return false;
        }
    }
}
