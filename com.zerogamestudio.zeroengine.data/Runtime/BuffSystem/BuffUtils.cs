using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.StatSystem;

namespace ZeroEngine.BuffSystem
{
    /// <summary>
    /// Buff 工具类 - 提供便捷的 Stat-Buff 集成方法 (v1.2.0+)
    /// </summary>
    public static class BuffUtils
    {
        #region Quick Buff Creation

        /// <summary>
        /// 创建临时 Buff 数据（用于运行时动态生成的 Buff）
        /// </summary>
        public static BuffData CreateTempBuff(
            string buffId,
            float duration,
            int maxStacks = 1,
            BuffCategory category = BuffCategory.Buff)
        {
            var data = ScriptableObject.CreateInstance<BuffData>();
            data.BuffId = buffId;
            data.Duration = duration;
            data.MaxStacks = maxStacks;
            data.Category = category;
            data.StackMode = BuffStackMode.Stack;
            data.ExpireMode = BuffExpireMode.RemoveAllStacks;
            return data;
        }

        /// <summary>
        /// 创建属性修改 Buff
        /// </summary>
        public static BuffData CreateStatBuff(
            string buffId,
            StatId statId,
            float value,
            StatModType modType,
            float duration,
            int maxStacks = 1)
        {
            var data = CreateTempBuff(buffId, duration, maxStacks);
            data.StatModifiers.Add(new BuffStatModifierConfig
            {
                StatId = statId,
                Value = value,
                ModType = modType
            });
            return data;
        }

        /// <summary>
        /// 创建多属性修改 Buff
        /// </summary>
        public static BuffData CreateMultiStatBuff(
            string buffId,
            float duration,
            int maxStacks,
            params (StatId id, float value, StatModType modType)[] modifiers)
        {
            var data = CreateTempBuff(buffId, duration, maxStacks);
            foreach (var (id, value, modType) in modifiers)
            {
                data.StatModifiers.Add(new BuffStatModifierConfig
                {
                    StatId = id,
                    Value = value,
                    ModType = modType
                });
            }
            return data;
        }

        #endregion

        #region Common Buff Patterns

        /// <summary>
        /// 创建增益 Buff (百分比加成)
        /// </summary>
        public static BuffData CreateBoostBuff(
            string buffId,
            StatId statId,
            float percentBoost,
            float duration)
        {
            return CreateStatBuff(
                buffId,
                statId,
                percentBoost / 100f,
                StatModType.PercentAdd,
                duration
            );
        }

        /// <summary>
        /// 创建减益 Buff (百分比削弱)
        /// </summary>
        public static BuffData CreateDebuffBuff(
            string buffId,
            StatId statId,
            float percentReduce,
            float duration)
        {
            return CreateStatBuff(
                buffId,
                statId,
                -percentReduce / 100f,
                StatModType.PercentAdd,
                duration
            );
        }

        /// <summary>
        /// 创建固定加成 Buff
        /// </summary>
        public static BuffData CreateFlatBuff(
            string buffId,
            StatId statId,
            float flatValue,
            float duration,
            int maxStacks = 1)
        {
            return CreateStatBuff(
                buffId,
                statId,
                flatValue,
                StatModType.Flat,
                duration,
                maxStacks
            );
        }

        /// <summary>
        /// 创建 DOT (Damage Over Time) Buff
        /// </summary>
        public static BuffData CreateDotBuff(
            string buffId,
            float duration,
            float tickInterval,
            int maxStacks = 1)
        {
            var data = CreateTempBuff(buffId, duration, maxStacks, BuffCategory.Debuff);
            data.TickInterval = tickInterval;
            return data;
        }

        /// <summary>
        /// 创建 HOT (Heal Over Time) Buff
        /// </summary>
        public static BuffData CreateHotBuff(
            string buffId,
            float duration,
            float tickInterval,
            int maxStacks = 1)
        {
            var data = CreateTempBuff(buffId, duration, maxStacks, BuffCategory.Buff);
            data.TickInterval = tickInterval;
            return data;
        }

        #endregion

        #region BuffReceiver Extensions

        /// <summary>
        /// 添加快速属性 Buff
        /// </summary>
        public static BuffHandler AddStatBuff(
            this BuffReceiver receiver,
            string buffId,
            StatId statId,
            float value,
            StatModType modType,
            float duration,
            int stacks = 1)
        {
            var data = CreateStatBuff(buffId, statId, value, modType, duration);
            return receiver.AddBuff(data, stacks);
        }

        /// <summary>
        /// 添加百分比增益
        /// </summary>
        public static BuffHandler AddPercentBoost(
            this BuffReceiver receiver,
            string buffId,
            StatId statId,
            float percentBoost,
            float duration)
        {
            var data = CreateBoostBuff(buffId, statId, percentBoost, duration);
            return receiver.AddBuff(data);
        }

        /// <summary>
        /// 添加百分比减益
        /// </summary>
        public static BuffHandler AddPercentDebuff(
            this BuffReceiver receiver,
            string buffId,
            StatId statId,
            float percentReduce,
            float duration)
        {
            var data = CreateDebuffBuff(buffId, statId, percentReduce, duration);
            return receiver.AddBuff(data);
        }

        /// <summary>
        /// 添加固定值增益
        /// </summary>
        public static BuffHandler AddFlatBoost(
            this BuffReceiver receiver,
            string buffId,
            StatId statId,
            float flatValue,
            float duration,
            int stacks = 1)
        {
            var data = CreateFlatBuff(buffId, statId, flatValue, duration);
            return receiver.AddBuff(data, stacks);
        }

        #endregion

        #region Query Helpers

        /// <summary>
        /// 获取所有增益 Buff
        /// </summary>
        public static IEnumerable<BuffHandler> GetBuffsByCategory(
            this BuffReceiver receiver,
            BuffCategory category)
        {
            foreach (var kvp in receiver.ActiveBuffs)
            {
                if (kvp.Value.Data.Category == category)
                {
                    yield return kvp.Value;
                }
            }
        }

        /// <summary>
        /// 获取所有增益
        /// </summary>
        public static IEnumerable<BuffHandler> GetAllBuffs(this BuffReceiver receiver)
        {
            return receiver.GetBuffsByCategory(BuffCategory.Buff);
        }

        /// <summary>
        /// 获取所有减益
        /// </summary>
        public static IEnumerable<BuffHandler> GetAllDebuffs(this BuffReceiver receiver)
        {
            return receiver.GetBuffsByCategory(BuffCategory.Debuff);
        }

        /// <summary>
        /// 获取影响指定属性的所有 Buff
        /// </summary>
        public static IEnumerable<BuffHandler> GetBuffsAffectingStat(
            this BuffReceiver receiver,
            StatId statId)
        {
            foreach (var kvp in receiver.ActiveBuffs)
            {
                var data = kvp.Value.Data;
                if (data.StatModifiers != null)
                {
                    foreach (var mod in data.StatModifiers)
                    {
                        if (mod.StatId == statId)
                        {
                            yield return kvp.Value;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 计算所有 Buff 对指定属性的总加成值
        /// </summary>
        public static float GetTotalStatModification(
            this BuffReceiver receiver,
            StatId statId,
            StatModType modType)
        {
            float total = 0f;

            foreach (var kvp in receiver.ActiveBuffs)
            {
                var handler = kvp.Value;
                var data = handler.Data;

                if (data.StatModifiers != null)
                {
                    foreach (var mod in data.StatModifiers)
                    {
                        if (mod.StatId == statId && mod.ModType == modType)
                        {
                            total += mod.Value * handler.CurrentStacks;
                        }
                    }
                }
            }

            return total;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// 移除所有减益
        /// </summary>
        public static void RemoveAllDebuffs(this BuffReceiver receiver)
        {
            var debuffs = new List<string>();
            foreach (var kvp in receiver.ActiveBuffs)
            {
                if (kvp.Value.Data.Category == BuffCategory.Debuff)
                {
                    debuffs.Add(kvp.Key);
                }
            }

            foreach (var id in debuffs)
            {
                receiver.RemoveBuffCompletely(id);
            }
        }

        /// <summary>
        /// 移除所有增益
        /// </summary>
        public static void RemoveAllBuffsByCategory(this BuffReceiver receiver, BuffCategory category)
        {
            var toRemove = new List<string>();
            foreach (var kvp in receiver.ActiveBuffs)
            {
                if (kvp.Value.Data.Category == category)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                receiver.RemoveBuffCompletely(id);
            }
        }

        #endregion
    }
}
