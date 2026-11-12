using UnityEngine;
using ZeroEngine.Combat;
using ZeroEngine.Utils;

namespace ZeroEngine.RPG.Systems
{
    /// <summary>
    /// BP 伤害处理器 - 根据 Boost 等级增加伤害
    /// </summary>
    public class BoostDamageProcessor : IDamageProcessor
    {
        /// <summary>
        /// 处理器优先级 (较早执行，在基础伤害计算后)
        /// </summary>
        public int Priority => 100;

        /// <summary>
        /// 处理伤害数据
        /// </summary>
        public DamageData ProcessDamage(DamageData damage, ICombatant target, DamageCalculationContext context)
        {
            // 检查是否有 Boost 数据
            var boostData = GetBoostData(damage);
            if (boostData == null || boostData.BoostLevel <= 0)
            {
                return damage;
            }

            int boostLevel = Mathf.Clamp(boostData.BoostLevel, 0, BoostConstants.MAX_BOOST_LEVEL);
            float multiplier = BoostConstants.GetDamageMultiplier(boostLevel);

            float newDamage = damage.BaseDamage * multiplier;

            DebugUtils.Log($"[BoostDamageProcessor] Boost Lv.{boostLevel}: {damage.BaseDamage:F1} x {multiplier:F2} = {newDamage:F1}");

            return damage.WithBaseDamage(newDamage);
        }

        /// <summary>
        /// 从伤害数据中获取 Boost 信息
        /// </summary>
        private BoostDamageData GetBoostData(DamageData damage)
        {
            // 直接检查 BoostDamageData
            if (damage.CustomData is BoostDamageData boostData)
            {
                return boostData;
            }

            // 检查 CombinedDamageData
            if (damage.CustomData is CombinedDamageData combined)
            {
                return combined.BoostData;
            }

            return null;
        }
    }

    /// <summary>
    /// Boost 伤害附加数据 - 存储在 DamageData.CustomData 中
    /// </summary>
    public class BoostDamageData
    {
        /// <summary>
        /// Boost 等级 (0-3)
        /// </summary>
        public int BoostLevel { get; set; }

        /// <summary>
        /// 是否已应用 Boost 加成
        /// </summary>
        public bool Applied { get; set; }

        /// <summary>
        /// 创建 Boost 伤害数据
        /// </summary>
        public static BoostDamageData Create(int boostLevel)
        {
            return new BoostDamageData
            {
                BoostLevel = Mathf.Clamp(boostLevel, 0, BoostConstants.MAX_BOOST_LEVEL),
                Applied = false
            };
        }
    }

    /// <summary>
    /// DamageData 扩展方法 - 用于添加 Boost 信息
    /// </summary>
    public static class BoostDamageExtensions
    {
        /// <summary>
        /// 为伤害数据添加 Boost 等级
        /// </summary>
        /// <param name="damage">原始伤害数据</param>
        /// <param name="boostLevel">Boost 等级 (0-3)</param>
        /// <returns>带有 Boost 数据的伤害</returns>
        public static DamageData WithBoost(this DamageData damage, int boostLevel)
        {
            if (boostLevel <= 0) return damage;

            damage.CustomData = BoostDamageData.Create(boostLevel);
            return damage;
        }

        /// <summary>
        /// 获取伤害数据中的 Boost 等级
        /// </summary>
        /// <param name="damage">伤害数据</param>
        /// <returns>Boost 等级，如果没有则返回 0</returns>
        public static int GetBoostLevel(this DamageData damage)
        {
            var boostData = damage.CustomData as BoostDamageData;
            return boostData?.BoostLevel ?? 0;
        }

        /// <summary>
        /// 检查伤害是否有 Boost
        /// </summary>
        public static bool HasBoost(this DamageData damage)
        {
            var boostData = damage.CustomData as BoostDamageData;
            return boostData != null && boostData.BoostLevel > 0;
        }
    }
}
