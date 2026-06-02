using System;
using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.Inventory;
using ZeroEngine.StatSystem;

namespace ZeroEngine.Equipment
{
    /// <summary>
    /// 装备属性修饰数据
    /// </summary>
    [Serializable]
    public class StatModifierData
    {
        [Tooltip("属性类型")]
        public StatType StatType;

        public StatId StatId
        {
            get => new StatId(StatType.ToString());
            set => StatType = value.ToStatTypeOrDefault();
        }

        [Tooltip("修饰类型")]
        public StatModType ModType = StatModType.Flat;

        [Tooltip("基础值")]
        public float BaseValue;

        [Tooltip("每强化等级增加值")]
        public float ValuePerLevel;

        /// <summary>
        /// 获取指定强化等级的修饰值
        /// </summary>
        public float GetValue(int enhanceLevel)
        {
            return BaseValue + ValuePerLevel * enhanceLevel;
        }

        /// <summary>
        /// 创建 StatModifier
        /// </summary>
        public StatModifier CreateModifier(int enhanceLevel, object source)
        {
            return new StatModifier(GetValue(enhanceLevel), ModType, (int)ModType, source);
        }
    }

    /// <summary>
    /// 装备数据定义
    /// 继承自 InventoryItemSO，扩展装备特有属性
    /// </summary>
    [CreateAssetMenu(fileName = "NewEquipment", menuName = "ZeroEngine/Equipment/Equipment Data")]
    public class EquipmentDataSO : InventoryItemSO
    {
        [Header("装备配置")]
        [Tooltip("装备槽位类型")]
        public EquipmentSlotType SlotType;

        [Tooltip("所属套装（可选）")]
        public EquipmentSetSO BelongsToSet;

        [Header("属性")]
        [Tooltip("基础属性列表")]
        public List<StatModifierData> BaseStats = new List<StatModifierData>();

        [Header("强化配置")]
        [Tooltip("最大强化等级")]
        public int MaxEnhanceLevel = 15;

        [Tooltip("最大精炼等级")]
        public int MaxRefineLevel = 5;

        [Tooltip("宝石槽数量")]
        public int GemSlotCount = 0;

        [Tooltip("每精炼等级解锁的宝石槽数")]
        public int GemSlotsPerRefine = 1;

        [Header("强化公式")]
        [Tooltip("强化成功率基础值 (0-1)")]
        [Range(0f, 1f)]
        public float BaseEnhanceSuccessRate = 1f;

        [Tooltip("每级成功率衰减")]
        [Range(0f, 0.1f)]
        public float SuccessRateDecayPerLevel = 0.05f;

        [Header("需求")]
        [Tooltip("装备等级需求")]
        public int RequiredLevel = 1;

        /// <summary>
        /// 获取指定等级的强化成功率
        /// </summary>
        public float GetEnhanceSuccessRate(int currentLevel)
        {
            float rate = BaseEnhanceSuccessRate - (currentLevel * SuccessRateDecayPerLevel);
            return Mathf.Clamp01(rate);
        }

        /// <summary>
        /// 获取解锁的宝石槽数量
        /// </summary>
        public int GetUnlockedGemSlots(int refineLevel)
        {
            int baseSlots = GemSlotCount > 0 ? 1 : 0;
            int bonusSlots = refineLevel * GemSlotsPerRefine;
            return Mathf.Min(baseSlots + bonusSlots, GemSlotCount);
        }

        /// <summary>
        /// 创建装备实例
        /// </summary>
        public EquipmentInstance CreateInstance()
        {
            return new EquipmentInstance(this);
        }

        private void OnValidate()
        {
            // 确保类型为装备
            Type = InventoryItemType.Equip;

            // 装备不可堆叠
            MaxStack = 1;
        }
    }
}
